using System.Windows.Threading;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal readonly record struct InfractionRecord(InfractionKind Kind, string Detail, DateTimeOffset At);

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
    private DateTimeOffset? _punishmentUntil;
    private bool _active;
    private DispatcherTimer? _timer;
    private bool _disposed;

    public PunishmentService(Dispatcher dispatcher, Func<int> baseStrictnessProvider)
    {
        _dispatcher = dispatcher;
        _baseStrictnessProvider = baseStrictnessProvider;

        var stored = _store.Load();
        _settings = stored.ToSettings();
        _floorIndex = Math.Clamp(stored.FloorIndex, 0, AgentModeRegistry.MaxStrictnessIndex);
        _infractionCount = Math.Max(0, stored.InfractionCount);
        _punishmentUntil = stored.PunishmentUntil;

        CatchUpDecays(DateTimeOffset.UtcNow, persist: true, notify: true);

        _timer = new DispatcherTimer(DecayCheckInterval, DispatcherPriority.Background, OnDecayTick, dispatcher);
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
            record = new InfractionRecord(kind, detail, now);
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
                _infractionCount++;
                var threshold = _settings.ThresholdForFloor(_floorIndex);
                var triggeredEscalation = _infractionCount >= threshold;

                if (triggeredEscalation)
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

                    _punishmentUntil = Max(now, _punishmentUntil ?? now) + EscalationDuration;
                    _infractionCount = 0;
                }
                else if (_floorIndex > 0)
                {
                    _punishmentUntil = Max(now, _punishmentUntil ?? now)
                        + ExtensionDurationFor(kind);
                }

                Persist();
            }
        }

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
            if (_floorIndex != 0 || _punishmentUntil is not null)
                changed = true;

            _floorIndex = 0;
            _punishmentUntil = null;
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
            if (_floorIndex != 0 || _infractionCount != 0 || _punishmentUntil is not null)
                changed = true;

            _floorIndex = 0;
            _infractionCount = 0;
            _punishmentUntil = null;
            _appHits.Clear();
            _lastCounted.Clear();
            Persist();
        }

        if (changed)
            FloorLevelChanged?.Invoke(0);

        StateChanged?.Invoke();
    }

    private void OnDecayTick(object? sender, EventArgs e) =>
        CatchUpDecays(DateTimeOffset.UtcNow, persist: true, notify: true);

    private void CatchUpDecays(DateTimeOffset now, bool persist, bool notify)
    {
        var floorChanged = false;

        lock (_lock)
        {
            while (_floorIndex > 0 && _punishmentUntil is { } until && now >= until)
            {
                _floorIndex--;
                _infractionCount = 0;
                floorChanged = true;
                _punishmentUntil = _floorIndex > 0 ? until + EscalationDuration : null;
            }

            if (floorChanged && persist)
                Persist();
        }

        if (floorChanged && notify)
        {
            FloorLevelChanged?.Invoke(_floorIndex);
            StateChanged?.Invoke();
        }
    }

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
                PunishmentUntil = _punishmentUntil?.ToString("o"),
                SecondsUntilDecay = secondsUntilDecay,
                RecentInfractions = recent,
            };
        }
    }

    private TimeSpan EscalationDuration =>
        new DurationParts(_settings.EscalationHours, _settings.EscalationMinutes)
            .ToTimeSpan(Config.TestingShortPunishment);

    private TimeSpan ExtensionDurationFor(InfractionKind kind) =>
        _settings.InfractionExtensions.For(kind).ToTimeSpan(Config.TestingShortPunishment);

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private void Persist()
    {
        _store.Save(new PunishmentStore.StoredPunishment
        {
            FloorIndex = _floorIndex,
            InfractionCount = _infractionCount,
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
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Stop();
        _timer = null;
    }
}
