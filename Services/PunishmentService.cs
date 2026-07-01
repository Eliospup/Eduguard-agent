using System.Linq;
using System.Windows.Threading;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal readonly record struct InfractionRecord(InfractionKind Kind, string Detail, DateTimeOffset At, int TrustPointsLost);

/// <summary>
/// Tracks infractions and auto-escalates the enforced strictness level. The punishment level
/// is an absolute "floor": the effective mode is the stricter of the Dom-set base mode and this
/// floor. Each escalation arms a decay timer; the floor drops one level once enough
/// infraction-free time elapses, so returning to the base mode requires sustained good behaviour.
/// </summary>
internal sealed class PunishmentService : IDisposable
{
    private static readonly TimeSpan DecayCheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BlockedAppWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LimitCooldown = TimeSpan.FromMinutes(5);
    private const int BlockedAppThreshold = 3;
    private const int MaxRecentInfractions = 50;
    private const int MaxRecentPersisted = 25;

    private readonly Dispatcher _dispatcher;
    private readonly Func<int> _baseStrictnessProvider;
    private readonly PunishmentStore _store = new();
    private readonly object _lock = new();

    private readonly Dictionary<string, DateTimeOffset> _lastCounted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<DateTimeOffset>> _appHits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<InfractionEventPayload> _pendingReport = new();

    private PunishmentSettings _settings;
    private int _floorIndex;
    private int _infractionCount;
    private double _trust;
    private DateTimeOffset? _punishmentUntil;
    private bool _active;
    private DispatcherTimer? _timer;
    private bool _disposed;
    private readonly List<InfractionRecord> _recentPersistedInfractions = new();

    public PunishmentService(Dispatcher dispatcher, Func<int> baseStrictnessProvider)
    {
        _dispatcher = dispatcher;
        _baseStrictnessProvider = baseStrictnessProvider;

        var stored = _store.Load();
        _settings = stored.ToSettings();
        _floorIndex = Math.Clamp(stored.FloorIndex, 0, AgentModeRegistry.MaxStrictnessIndex);
        _infractionCount = Math.Max(0, stored.InfractionCount);
        _trust = Math.Clamp(stored.Trust, 0, PunishmentSettings.MaxTrust);

        if (stored.RecentInfractions is { } storedInfractions)
        {
            foreach (var si in storedInfractions)
            {
                if (Enum.TryParse<InfractionKind>(si.Kind, out var kind)
                    && DateTimeOffset.TryParse(si.At, out var at))
                {
                    _recentPersistedInfractions.Add(new InfractionRecord(kind, si.Detail, at, si.TrustPointsLost));
                }
            }
        }

        // Offline time earns no trust back (no supervision = no clean-time credit); resume
        // from the stored gauge and recompute the "time to earn back a level" countdown.
        RecomputePunishmentUntil(DateTimeOffset.UtcNow);

        _timer = new DispatcherTimer(DecayCheckInterval, DispatcherPriority.Background, OnRegenTick, dispatcher);
    }

    /// <summary>Raised whenever the floor level changes (escalation or decay). Carries the new floor index.</summary>
    public event Action<int>? FloorLevelChanged;

    /// <summary>Raised on a fresh escalation: (fromStrictnessIndex, toStrictnessIndex).</summary>
    public event Action<int, int>? Escalated;

    /// <summary>Raised for every counted infraction so the UI can log it.</summary>
    public event Action<InfractionRecord>? InfractionRegistered;

    /// <summary>Raised when count / floor / timer change so the UI can refresh summaries.</summary>
    public event Action? StateChanged;

    public int FloorLevelIndex
    {
        get { lock (_lock) return _floorIndex; }
    }

    public int InfractionCount
    {
        get { lock (_lock) return _infractionCount; }
    }

    /// <summary>Current trust gauge, 0-100.</summary>
    public int TrustValue
    {
        get { lock (_lock) return (int)Math.Round(_trust); }
    }

    /// <summary>Which trust zone the gauge is in (drives tone + escalation).</summary>
    public TrustZone Zone
    {
        get { lock (_lock) return PunishmentSettings.ZoneFor((int)Math.Round(_trust)); }
    }

    public int Threshold
    {
        get
        {
            lock (_lock)
                return _settings.ThresholdForFloor(_floorIndex);
        }
    }

    public bool Enabled
    {
        get { lock (_lock) return _settings.Enabled; }
    }

    public IReadOnlyList<InfractionRecord> RecentInfractions => _recentPersistedInfractions;

    public DateTimeOffset? PunishmentUntil
    {
        get { lock (_lock) return _punishmentUntil; }
    }

    public PunishmentSettings Settings
    {
        get { lock (_lock) return _settings; }
    }

    public void Start() => _active = true;

    public void Stop() => _active = false;

    public void RegisterInfraction(InfractionKind kind, string key, string detail)
    {
        if (_dispatcher.CheckAccess())
            RegisterInfractionCore(kind, key, detail);
        else
            _dispatcher.BeginInvoke(() => RegisterInfractionCore(kind, key, detail));
    }

    private void RegisterInfractionCore(InfractionKind kind, string key, string detail)
    {
        if (!_active || _disposed)
            return;

        lock (_lock)
        {
            if (!_settings.InfractionKinds.IsEnabled(kind))
                return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!ShouldCount(kind, key, now))
            return;

        InfractionRecord record;
        int? escalatedFrom = null;
        int? escalatedTo = null;
        var floorChanged = false;

        lock (_lock)
        {
            // Only a real, enforced gauge drop counts as "points lost" — when discipline is
            // disabled the infraction is still logged but nothing was actually deducted.
            var pointsLost = _settings.Enabled ? _settings.InfractionWeights.For(kind) : 0;
            record = new InfractionRecord(kind, detail, now, pointsLost);
            _pendingReport.Enqueue(new InfractionEventPayload
            {
                Kind = kind.ToApiKey(),
                Detail = detail,
                At = now.ToString("o"),
            });
            while (_pendingReport.Count > MaxRecentInfractions)
                _pendingReport.Dequeue();

            if (!_settings.Enabled)
            {
                Persist();
            }
            else
            {
                // Drop the gauge by the kind's weight; bottoming out bumps the mode floor.
                _trust = Math.Max(0, _trust - _settings.InfractionWeights.For(kind));
                _infractionCount++;

                if (_trust <= PunishmentSettings.EscalationThreshold)
                {
                    var effective = Math.Max(_baseStrictnessProvider(), _floorIndex);
                    var target = Math.Min(effective + 1, AgentModeRegistry.MaxStrictnessIndex);

                    if (target > _floorIndex)
                    {
                        escalatedFrom = effective;
                        escalatedTo = target;
                        _floorIndex = target;
                        floorChanged = true;
                    }

                    // Reset above the escalation band so a single extra slip doesn't chain
                    // escalations; the gauge then regenerates back toward full.
                    _trust = PunishmentSettings.ReescalateReset;
                    _infractionCount = 0;
                }

                RecomputePunishmentUntil(now);
                Persist();
            }
        }

        _recentPersistedInfractions.Insert(0, record);
        while (_recentPersistedInfractions.Count > MaxRecentPersisted)
            _recentPersistedInfractions.RemoveAt(_recentPersistedInfractions.Count - 1);

        InfractionRegistered?.Invoke(record);
        if (floorChanged)
        {
            FloorLevelChanged?.Invoke(_floorIndex);
            if (escalatedFrom is { } from && escalatedTo is { } to)
                Escalated?.Invoke(from, to);
        }

        StateChanged?.Invoke();
    }

    private bool ShouldCount(InfractionKind kind, string key, DateTimeOffset now)
    {
        var bucket = $"{kind}|{key}";

        lock (_lock)
        {
            if (kind == InfractionKind.BlockedAppRepeated)
            {
                if (!_appHits.TryGetValue(bucket, out var hits))
                {
                    hits = [];
                    _appHits[bucket] = hits;
                }

                hits.Add(now);
                hits.RemoveAll(t => now - t > BlockedAppWindow);

                if (hits.Count < BlockedAppThreshold)
                    return false;

                hits.Clear();
                _lastCounted[bucket] = now;
                return true;
            }

            // VPN attempts are surfaced every time the shield kills one — no debounce.
            if (kind == InfractionKind.VpnAttempt)
                return true;

            var cooldown = kind == InfractionKind.LimitIgnored ? LimitCooldown : DefaultCooldown;
            if (_lastCounted.TryGetValue(bucket, out var last) && now - last < cooldown)
                return false;

            _lastCounted[bucket] = now;
            return true;
        }
    }

    /// <summary>Merges a (possibly partial) Dom-pushed settings payload over the current config.</summary>
    public void ApplySettings(PunishmentSettingsPayload payload)
    {
        if (_dispatcher.CheckAccess())
            ApplySettingsCore(payload);
        else
            _dispatcher.BeginInvoke(() => ApplySettingsCore(payload));
    }

    private void ApplySettingsCore(PunishmentSettingsPayload payload)
    {
        lock (_lock)
        {
            var merged = MergeSettings(_settings, payload);
            _settings = merged.Sanitized();
            Persist();
        }

        StateChanged?.Invoke();
    }

    public static PunishmentSettings MergeSettingsForResponse(
        PunishmentSettings current,
        PunishmentSettingsPayload payload) =>
        MergeSettings(current, payload).Sanitized();

    private static PunishmentSettings MergeSettings(PunishmentSettings current, PunishmentSettingsPayload payload)
    {
        var legacyThreshold = payload.InfractionThreshold;
        var legacyExtension = payload.InfractionExtensionHours is { } legacyHours and > 0
            ? DurationParts.FromTotalMinutes((int)Math.Round(legacyHours * 60))
            : (DurationParts?)null;

        var extensions = current.InfractionExtensions.Merge(payload.InfractionExtensions);
        if (legacyExtension is { } legacy && payload.InfractionExtensions is null)
            extensions = ApplyLegacyExtensionToAll(extensions, legacy);

        return new PunishmentSettings
        {
            Enabled = payload.Enabled ?? current.Enabled,
            ThresholdTrustedToSub = payload.ThresholdTrustedToSub
                ?? legacyThreshold
                ?? current.ThresholdTrustedToSub,
            ThresholdSubToRestricted = payload.ThresholdSubToRestricted
                ?? legacyThreshold
                ?? current.ThresholdSubToRestricted,
            EscalationHours = payload.EscalationHours ?? current.EscalationHours,
            EscalationMinutes = payload.EscalationMinutes ?? current.EscalationMinutes,
            RegenPerHour = payload.RegenPerHour ?? current.RegenPerHour,
            InfractionWeights = current.InfractionWeights.Merge(payload.InfractionWeights),
            InfractionKinds = current.InfractionKinds.Merge(payload.InfractionKinds),
            InfractionExtensions = extensions,
        };
    }

    private static InfractionExtensionSettings ApplyLegacyExtensionToAll(
        InfractionExtensionSettings extensions,
        DurationParts legacy) =>
        new()
        {
            VpnAttempt = legacy,
            BlockedAppRepeated = legacy,
            BypassAttempt = legacy,
            LimitIgnored = legacy,
            StudyTimeViolation = legacy,
            BlockedSearch = legacy,
        };

    public void ClearFloor()
    {
        if (_dispatcher.CheckAccess())
            ClearFloorCore();
        else
            _dispatcher.BeginInvoke(ClearFloorCore);
    }

    private void ClearFloorCore()
    {
        var changed = false;
        lock (_lock)
        {
            if (_floorIndex != 0 || _punishmentUntil is not null || _trust < PunishmentSettings.MaxTrust)
                changed = true;

            _floorIndex = 0;
            _trust = PunishmentSettings.MaxTrust;
            _punishmentUntil = null;
            _recentPersistedInfractions.Clear();
            Persist();
        }

        if (changed)
            FloorLevelChanged?.Invoke(0);

        StateChanged?.Invoke();
    }

    public void Reset()
    {
        if (_dispatcher.CheckAccess())
            ResetCore();
        else
            _dispatcher.BeginInvoke(ResetCore);
    }

    private void ResetCore()
    {
        var changed = false;
        lock (_lock)
        {
            if (_floorIndex != 0 || _infractionCount != 0 || _punishmentUntil is not null
                || _trust < PunishmentSettings.MaxTrust)
                changed = true;

            _floorIndex = 0;
            _infractionCount = 0;
            _trust = PunishmentSettings.MaxTrust;
            _punishmentUntil = null;
            _appHits.Clear();
            _lastCounted.Clear();
            _recentPersistedInfractions.Clear();
            Persist();
        }

        if (changed)
            FloorLevelChanged?.Invoke(0);

        StateChanged?.Invoke();
    }

    private void OnRegenTick(object? sender, EventArgs e)
    {
        // Trust only regenerates during supervised time — being off earns no credit.
        if (!_active)
            return;

        var now = DateTimeOffset.UtcNow;
        var floorChanged = false;

        lock (_lock)
        {
            if (!_settings.Enabled || _trust >= PunishmentSettings.MaxTrust && _floorIndex == 0)
            {
                RecomputePunishmentUntil(now);
                return;
            }

            _trust = Math.Min(PunishmentSettings.MaxTrust, _trust + RegenPerTick);

            // Reaching full trust at a punished level earns one step back down.
            if (_trust >= PunishmentSettings.MaxTrust && _floorIndex > 0)
            {
                _floorIndex--;
                _infractionCount = 0;
                floorChanged = true;
                _trust = _floorIndex > 0 ? PunishmentSettings.StepDownReset : PunishmentSettings.MaxTrust;
            }

            RecomputePunishmentUntil(now);
            Persist();
        }

        if (floorChanged)
            FloorLevelChanged?.Invoke(_floorIndex);

        StateChanged?.Invoke();
    }

    /// <summary>Synthetic "time to earn back a level" — when the gauge will next reach full.</summary>
    private void RecomputePunishmentUntil(DateTimeOffset now)
    {
        if (_floorIndex <= 0)
        {
            _punishmentUntil = null;
            return;
        }

        var perHour = Math.Max(1, _settings.RegenPerHour) * (Config.TestingShortPunishment ? 60 : 1);
        var hoursToFull = Math.Max(0, PunishmentSettings.MaxTrust - _trust) / perHour;
        _punishmentUntil = now + TimeSpan.FromHours(hoursToFull);
    }

    /// <summary>Trust points regained each timer tick (accelerated in the testing build).</summary>
    private double RegenPerTick =>
        _settings.RegenPerHour * DecayCheckInterval.TotalHours * (Config.TestingShortPunishment ? 60 : 1);

    public void ApplyTo(HeartbeatRequest request)
    {
        lock (_lock)
        {
            var baseIndex = Math.Clamp(_baseStrictnessProvider(), 0, AgentModeRegistry.MaxStrictnessIndex);
            var effectiveIndex = Math.Max(baseIndex, _floorIndex);
            var now = DateTimeOffset.UtcNow;

            InfractionEventPayload[]? recent = null;
            if (_pendingReport.Count > 0)
            {
                recent = _pendingReport.ToArray();
                _pendingReport.Clear();
            }

            int? secondsUntilDecay = null;
            if (_floorIndex > 0 && _punishmentUntil is { } until)
                secondsUntilDecay = Math.Max(0, (int)Math.Ceiling((until - now).TotalSeconds));

            // Always send the full runtime snapshot so the dashboard stays in sync with the agent.
            request.Punishment = new PunishmentStatePayload
            {
                BaseLevel = AgentModeRegistry.AtStrictnessIndex(baseIndex).Slug,
                EffectiveLevel = AgentModeRegistry.AtStrictnessIndex(effectiveIndex).Slug,
                FloorIndex = _floorIndex,
                IsPunished = _floorIndex > baseIndex,
                InfractionCount = _infractionCount,
                Trust = (int)Math.Round(_trust),
                TrustZone = PunishmentSettings.ZoneFor((int)Math.Round(_trust)).ToString(),
                PunishmentUntil = _punishmentUntil?.ToString("o"),
                SecondsUntilDecay = secondsUntilDecay,
                RecentInfractions = recent,
            };
        }
    }

    private void Persist()
    {
        var recentSnapshot = _recentPersistedInfractions
            .Select(r => new PunishmentStore.StoredInfractionRecord
            {
                Kind = r.Kind.ToString(),
                Detail = r.Detail,
                At = r.At.ToString("o"),
                TrustPointsLost = r.TrustPointsLost,
            })
            .ToList();

        _store.Save(new PunishmentStore.StoredPunishment
        {
            FloorIndex = _floorIndex,
            InfractionCount = _infractionCount,
            Trust = _trust,
            RegenPerHour = _settings.RegenPerHour,
            WeightVpn = _settings.InfractionWeights.VpnAttempt,
            WeightBypass = _settings.InfractionWeights.BypassAttempt,
            WeightBlockedApp = _settings.InfractionWeights.BlockedAppRepeated,
            WeightBlockedSearch = _settings.InfractionWeights.BlockedSearch,
            WeightStudy = _settings.InfractionWeights.StudyTimeViolation,
            WeightLimit = _settings.InfractionWeights.LimitIgnored,
            PunishmentUntil = _punishmentUntil,
            Enabled = _settings.Enabled,
            ThresholdTrustedToSub = _settings.ThresholdTrustedToSub,
            ThresholdSubToRestricted = _settings.ThresholdSubToRestricted,
            EscalationHours = _settings.EscalationHours,
            EscalationMinutes = _settings.EscalationMinutes,
            ExtensionVpnHours = _settings.InfractionExtensions.VpnAttempt.Hours,
            ExtensionVpnMinutes = _settings.InfractionExtensions.VpnAttempt.Minutes,
            ExtensionBlockedAppHours = _settings.InfractionExtensions.BlockedAppRepeated.Hours,
            ExtensionBlockedAppMinutes = _settings.InfractionExtensions.BlockedAppRepeated.Minutes,
            ExtensionBypassHours = _settings.InfractionExtensions.BypassAttempt.Hours,
            ExtensionBypassMinutes = _settings.InfractionExtensions.BypassAttempt.Minutes,
            ExtensionLimitHours = _settings.InfractionExtensions.LimitIgnored.Hours,
            ExtensionLimitMinutes = _settings.InfractionExtensions.LimitIgnored.Minutes,
            ExtensionStudyHours = _settings.InfractionExtensions.StudyTimeViolation.Hours,
            ExtensionStudyMinutes = _settings.InfractionExtensions.StudyTimeViolation.Minutes,
            ExtensionBlockedSearchHours = _settings.InfractionExtensions.BlockedSearch.Hours,
            ExtensionBlockedSearchMinutes = _settings.InfractionExtensions.BlockedSearch.Minutes,
            InfractionVpnAttempt = _settings.InfractionKinds.VpnAttempt,
            InfractionBlockedAppRepeated = _settings.InfractionKinds.BlockedAppRepeated,
            InfractionBypassAttempt = _settings.InfractionKinds.BypassAttempt,
            InfractionLimitIgnored = _settings.InfractionKinds.LimitIgnored,
            InfractionStudyTimeViolation = _settings.InfractionKinds.StudyTimeViolation,
            InfractionBlockedSearch = _settings.InfractionKinds.BlockedSearch,
            RecentInfractions = recentSnapshot,
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Stop();
        _timer = null;
    }
}
