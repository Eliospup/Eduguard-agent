using System.Runtime.Versioning;
using EduGuardAgent.Agent;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

[SupportedOSPlatform("windows")]
internal sealed class AgentModeService : IDisposable
{
    private readonly AgentModeStore _store = new();
    private readonly SecurityHardeningService _security;
    private AgentModeDefinition _baseDefinition = AgentModeRegistry.TrustedSub;
    private string? _displayNameOverride;
    private ModeFeatures _features = ModeFeatures.ForTrustedSub;
    private WeeklyMinuteLimits _screenTimeLimits = WeeklyMinuteLimits.Create(
        AgentModeRegistry.TrustedSub.Defaults.ScreenTimeLimitMinutes);

    /// <summary>Absolute punishment floor strictness index (0 = no active punishment).</summary>
    private int _floorIndex;

    public AgentModeService(SessionState sessionState, Action<string>? log = null)
    {
        _ = sessionState;
        _security = new SecurityHardeningService(log);
        _security.ToolBlocked += exe => SecurityToolBlocked?.Invoke(exe);
        LoadFromStorage();
    }

    public event Action? Changed;

    /// <summary>Raised when a hardened tool (Task Manager, Registry Editor, …) is force-closed.</summary>
    public event Action<string>? SecurityToolBlocked;

    /// <summary>Strictness rank of the Dom-set base mode (ignores any active punishment floor).</summary>
    public int BaseStrictnessIndex => AgentModeRegistry.StrictnessIndex(_baseDefinition.Slug);

    /// <summary>The mode actually enforced: the stricter of the base mode and the punishment floor.</summary>
    private AgentModeDefinition EffectiveDefinition
    {
        get
        {
            var effIndex = Math.Max(BaseStrictnessIndex, _floorIndex);
            return effIndex == BaseStrictnessIndex
                ? _baseDefinition
                : AgentModeRegistry.AtStrictnessIndex(effIndex);
        }
    }

    private bool IsPunished => _floorIndex > BaseStrictnessIndex;

    public bool IsPunishmentActive => IsPunished;

    private ModeFeatures EffectiveFeatures
    {
        get
        {
            if (!IsPunished)
                return _features;

            // Punishment raises baseline locks, but per-mode toggles saved in _features
            // (local settings / Dom) still apply — especially opt-out of process-killer defense.
            var punished = EffectiveDefinition.Features;
            return new ModeFeatures
            {
                BlockTaskManager = punished.BlockTaskManager || _features.BlockTaskManager,
                VpnShield = punished.VpnShield || _features.VpnShield,
                BlockRegistryEditor = punished.BlockRegistryEditor || _features.BlockRegistryEditor,
                BlockCommandPrompt = punished.BlockCommandPrompt || _features.BlockCommandPrompt,
                BlockPowerShell = punished.BlockPowerShell || _features.BlockPowerShell,
                BlockSystemConfig = punished.BlockSystemConfig || _features.BlockSystemConfig,
                BlockControlPanel = punished.BlockControlPanel || _features.BlockControlPanel,
                BlockProcessTools = punished.BlockProcessTools || _features.BlockProcessTools,
                BlockProcessKillers = punished.BlockProcessKillers && _features.BlockProcessKillers,
                KioskMode = punished.KioskMode || _features.KioskMode,
            };
        }
    }

    public string BaseSlug => _baseDefinition.Slug;

    public string BaseDisplayName => _displayNameOverride ?? _baseDefinition.DisplayName;

    public string Slug => EffectiveDefinition.Slug;

    public string DisplayName =>
        IsPunished ? EffectiveDefinition.DisplayName : (_displayNameOverride ?? _baseDefinition.DisplayName);

    public string ModeSubtitle =>
        string.IsNullOrWhiteSpace(EffectiveDefinition.ModeSubtitle)
            ? string.Format(EffectiveDefinition.Copy.LevelSubtitleFormat, DisplayName)
            : EffectiveDefinition.ModeSubtitle;

    public ModeCopySet Copy => EffectiveDefinition.Copy;

    public ModeTheme Theme => EffectiveDefinition.Theme;

    public ModeRuleDefaults Defaults => EffectiveDefinition.Defaults;

    public ModeFeatures Features => EffectiveFeatures;

    public int ScreenTimeLimitMinutes =>
        IsPunished
            ? EffectiveDefinition.Defaults.ScreenTimeLimitMinutes
            : _screenTimeLimits.ForDate(DateOnly.FromDateTime(DateTime.Now));

    public ModeUiPresentation Ui => EffectiveDefinition.Ui;

    public IReadOnlyList<LevelStep> BuildModeSteps()
    {
        var effectiveSlug = EffectiveDefinition.Slug;
        return AgentModeRegistry.All
            .Select(mode => new LevelStep
            {
                Name = mode.DisplayName,
                ShortLabel = mode.ShortLabel,
                IsCurrent = string.Equals(mode.Slug, effectiveSlug, StringComparison.Ordinal),
            })
            .ToList();
    }

    /// <summary>
    /// Applies a punishment floor (absolute strictness index). The base mode is never overwritten,
    /// so a later server <c>set_mode</c> below this floor is ignored until the floor decays or the
    /// Dom resets it. Returns true when the effective mode changed.
    /// </summary>
    public void SetPunishmentFloor(int floorIndex)
    {
        var clamped = Math.Clamp(floorIndex, 0, AgentModeRegistry.MaxStrictnessIndex);
        if (clamped == _floorIndex)
            return;

        var previousSlug = EffectiveDefinition.Slug;
        _floorIndex = clamped;
        ApplyEffective();

        if (!string.Equals(previousSlug, EffectiveDefinition.Slug, StringComparison.Ordinal))
            Changed?.Invoke();
    }

    public void Apply(ModeSettingsPayload? payload, ScreenTimeSettingsPayload? screenTime = null)
    {
        if (payload is null && screenTime is null)
            return;

        var changed = false;

        if (payload is not null)
        {
            if (payload.Slug is { } slug && AgentModeSlugs.IsKnown(slug))
            {
                var definition = AgentModeRegistry.Get(slug);
                _baseDefinition = definition;
                _displayNameOverride = string.IsNullOrWhiteSpace(payload.DisplayName) ? null : payload.DisplayName.Trim();
                _features = MergeFeatures(payload.Features, definition.Features);
                _screenTimeLimits = WeeklyMinuteLimits.Create(definition.Defaults.ScreenTimeLimitMinutes);
                changed = true;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(payload.DisplayName))
                {
                    _displayNameOverride = payload.DisplayName.Trim();
                    changed = true;
                }

                if (payload.Features is not null)
                {
                    _features = MergeFeatures(payload.Features, _features);
                    changed = true;
                }
            }
        }

        if (screenTime?.DailyLimitMinutes is not null || screenTime?.WeeklyLimits is not null)
        {
            _screenTimeLimits = WeeklyMinuteLimits.Parse(
                screenTime.DailyLimitMinutes,
                screenTime.WeeklyLimits,
                _screenTimeLimits,
                _screenTimeLimits.DefaultMinutes);
            changed = true;
        }

        if (!changed)
            return;

        ApplyEffective();
        Persist();
        Changed?.Invoke();
    }

    /// <summary>Applies the current effective mode (base ⊔ punishment floor) to copy, theme and locks.</summary>
    private void ApplyEffective()
    {
        var effective = EffectiveDefinition;
        UiCopy.ApplyTone(effective.Copy);
        ThemeService.Apply(effective.Theme, effective.Ui);
        SecurityRuntimeFlags.Persist(EffectiveFeatures);
        _security.Apply(EffectiveFeatures);
    }

    private static ModeFeatures MergeFeatures(ModeFeaturesPayload? payload, ModeFeatures basis) =>
        payload is null
            ? basis
            : new ModeFeatures
            {
                BlockTaskManager = payload.BlockTaskManager ?? basis.BlockTaskManager,
                VpnShield = payload.VpnShield ?? basis.VpnShield,
                BlockRegistryEditor = payload.BlockRegistryEditor ?? basis.BlockRegistryEditor,
                BlockCommandPrompt = payload.BlockCommandPrompt ?? basis.BlockCommandPrompt,
                BlockPowerShell = payload.BlockPowerShell ?? basis.BlockPowerShell,
                BlockSystemConfig = payload.BlockSystemConfig ?? basis.BlockSystemConfig,
                BlockControlPanel = payload.BlockControlPanel ?? basis.BlockControlPanel,
                BlockProcessTools = payload.BlockProcessTools ?? basis.BlockProcessTools,
                BlockProcessKillers = payload.BlockProcessKillers ?? basis.BlockProcessKillers,
                KioskMode = payload.KioskMode ?? basis.KioskMode,
            };

    private void LoadFromStorage()
    {
        var stored = _store.Load();
        _baseDefinition = AgentModeRegistry.Get(stored.Slug);
        _displayNameOverride = stored.DisplayName;
        _features = stored.Features;
        _screenTimeLimits = WeeklyMinuteLimits.Parse(
            stored.ScreenTimeLimitMinutes,
            stored.ScreenTimeWeeklyLimits,
            null,
            stored.ScreenTimeLimitMinutes);

        ApplyEffective();
    }

    private void Persist()
    {
        _store.Save(new AgentModeStore.StoredAgentMode
        {
            Slug = _baseDefinition.Slug,
            DisplayName = _displayNameOverride,
            ScreenTimeLimitMinutes = _screenTimeLimits.DefaultMinutes,
            ScreenTimeWeeklyLimits = _screenTimeLimits.HasOverrides
                ? WeeklyMinuteLimits.SerializeDays(_screenTimeLimits.DayMinutes)
                : null,
            Features = _features,
        });
    }

    public void Dispose() => _security.Dispose();
}
