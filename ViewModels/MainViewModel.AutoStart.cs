using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;
using EduGuardAgent.Services;

namespace EduGuardAgent.ViewModels;

internal enum SupervisionIndicatorState
{
    Inactive,
    Starting,
    Active,
    Offline,
}

internal partial class MainViewModel
{
    private readonly AgentPreferencesStore _agentPreferencesStore = new();
    private AgentPreferences _agentPreferences = new();
    private bool _autoStartEnabled;
    private bool _autoStartApplying;

    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        set
        {
            if (_autoStartApplying || !SetField(ref _autoStartEnabled, value))
                return;

            ApplyAutoStartPreference(value);
        }
    }

    public bool SupervisionIsActive => IsEnrolled || IsLocalMode;

    public bool SupervisionIndicatorIsInactive => ResolveSupervisionIndicatorState() == SupervisionIndicatorState.Inactive;

    public bool SupervisionIndicatorIsPending => ResolveSupervisionIndicatorState() == SupervisionIndicatorState.Starting;

    public bool SupervisionIndicatorIsActive => ResolveSupervisionIndicatorState() == SupervisionIndicatorState.Active;

    public bool SupervisionIndicatorIsOffline => ResolveSupervisionIndicatorState() == SupervisionIndicatorState.Offline;

    public string SupervisionStatusLabel => ResolveSupervisionIndicatorState() switch
    {
        SupervisionIndicatorState.Starting => UiCopy.SupervisionStatusStarting,
        SupervisionIndicatorState.Active => UiCopy.SupervisionStatusActive,
        SupervisionIndicatorState.Offline => UiCopy.SupervisionStatusOffline,
        _ => UiCopy.SupervisionStatusInactive,
    };

    public string WidgetSupervisionBadgeText => ResolveSupervisionIndicatorState() switch
    {
        SupervisionIndicatorState.Starting => UiCopy.SupervisionBadgeStarting,
        SupervisionIndicatorState.Active => UiCopy.SupervisionBadgeActive,
        SupervisionIndicatorState.Offline => UiCopy.SupervisionBadgeOffline,
        _ => UiCopy.SupervisionBadgeInactive,
    };

    private void InitializeAutoStart()
    {
        _agentPreferences = _agentPreferencesStore.Load();
        _autoStartApplying = true;
        _autoStartEnabled = ResolveAutoStartPreference();
        _autoStartApplying = false;
        OnPropertyChanged(nameof(AutoStartEnabled));
        ReconcileAutoStartTask();
        NotifySupervisionPresentationChanged();
    }

    private bool ResolveAutoStartPreference() =>
        _agentPreferences.AutoStartEnabled ?? WindowsAutoStartService.IsRegistered();

    private void EnsureAutoStartDefaultWhenSupervised()
    {
        if (_agentPreferences.AutoStartEnabled.HasValue)
            return;

        _agentPreferences.AutoStartEnabled = true;
        _agentPreferencesStore.Save(_agentPreferences);
        _autoStartApplying = true;
        _autoStartEnabled = true;
        _autoStartApplying = false;
        OnPropertyChanged(nameof(AutoStartEnabled));
        ReconcileAutoStartTask();
    }

    private void ApplyAutoStartPreference(bool enabled)
    {
        _agentPreferences.AutoStartEnabled = enabled;
        _agentPreferencesStore.Save(_agentPreferences);
        ReconcileAutoStartTask();
    }

    private void ReconcileAutoStartTask()
    {
        if (!ResolveAutoStartPreference())
        {
            if (!WindowsAutoStartService.TryDisable(out var disableError) && !string.IsNullOrWhiteSpace(disableError))
                AddLog($"Auto-start: {disableError}");
            return;
        }

        if (WindowsAutoStartService.TryEnable(out var error))
            return;

        if (!string.IsNullOrWhiteSpace(error))
            AddLog($"Auto-start: {error}");
    }

    private SupervisionIndicatorState ResolveSupervisionIndicatorState()
    {
        if (!IsEnrolled && !IsLocalMode)
            return SupervisionIndicatorState.Inactive;

        if (IsConnecting)
            return SupervisionIndicatorState.Starting;

        if (IsLocalMode)
            return SupervisionIndicatorState.Active;

        if (IsOnline)
            return SupervisionIndicatorState.Active;

        if (IsEnrolled && string.Equals(StatusText, UiCopy.StatusStarting, StringComparison.Ordinal))
            return SupervisionIndicatorState.Starting;

        return SupervisionIndicatorState.Offline;
    }

    private void NotifySupervisionPresentationChanged()
    {
        OnPropertyChanged(nameof(SupervisionIsActive));
        OnPropertyChanged(nameof(SupervisionIndicatorIsInactive));
        OnPropertyChanged(nameof(SupervisionIndicatorIsPending));
        OnPropertyChanged(nameof(SupervisionIndicatorIsActive));
        OnPropertyChanged(nameof(SupervisionIndicatorIsOffline));
        OnPropertyChanged(nameof(SupervisionStatusLabel));
        OnPropertyChanged(nameof(WidgetSupervisionBadgeText));
        OnPropertyChanged(nameof(WidgetToolTip));
        SyncWidgetPromptScheduler();
        RefreshTodayRules();
    }
}

