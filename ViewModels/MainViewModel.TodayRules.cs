using System.Collections.ObjectModel;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.ViewModels;

internal partial class MainViewModel
{
    public ObservableCollection<TodayRuleLine> TodayRules { get; } = [];
    public ObservableCollection<TodayRuleLine> AppTimeLimitRules { get; } = [];

    public bool ShowTodayRulesCard => IsEnrolled || IsLocalMode;

    public bool ShowAppTimeLimitsCard => IsLocalMode;

    public bool ShowAppTimeLimitsEmpty => ShowAppTimeLimitsCard && !HasAppTimeLimits;

    public string AppTimeLimitsCardTitle => UiCopy.AppTimeLimitsCardTitle;

    public string AppTimeLimitsCardSubtitle => UiCopy.AppTimeLimitsCardSubtitle;

    public string AppTimeLimitsEmptyMessage => UiCopy.AppTimeLimitsEmptyMessage;

    public string TodayRulesTitle => UiCopy.TodayRulesTitle;

    public string ConnectionStateTitle => ResolveConnectionState() switch
    {
        ConnectionPresentation.Local => UiCopy.ConnectionLocalTitle,
        ConnectionPresentation.Starting => UiCopy.ConnectionStartingTitle,
        ConnectionPresentation.Linked => UiCopy.ConnectionLinkedTitle,
        ConnectionPresentation.Offline => UiCopy.ConnectionOfflineTitle,
        _ => UiCopy.ConnectionOfflineTitle,
    };

    public string ConnectionStateBody => ResolveConnectionState() switch
    {
        ConnectionPresentation.Local => UiCopy.ConnectionLocalBody,
        ConnectionPresentation.Starting => UiCopy.ConnectionStartingBody,
        ConnectionPresentation.Linked => UiCopy.ConnectionLinkedBody,
        ConnectionPresentation.Offline => UiCopy.ConnectionOfflineBody,
        _ => UiCopy.ConnectionOfflineBody,
    };

    public bool ConnectionStateIsLinked =>
        ResolveConnectionState() is ConnectionPresentation.Linked or ConnectionPresentation.Local;

    public bool ConnectionStateIsPending =>
        ResolveConnectionState() == ConnectionPresentation.Starting;

    public bool ConnectionStateIsOffline =>
        ResolveConnectionState() == ConnectionPresentation.Offline;

    private enum ConnectionPresentation
    {
        Starting,
        Linked,
        Offline,
        Local,
    }

    private ConnectionPresentation ResolveConnectionState()
    {
        if (IsLocalMode)
            return ConnectionPresentation.Local;

        if (IsConnecting)
            return ConnectionPresentation.Starting;

        if (IsOnline)
            return ConnectionPresentation.Linked;

        if (IsEnrolled && string.Equals(StatusText, UiCopy.StatusStarting, StringComparison.Ordinal))
            return ConnectionPresentation.Starting;

        return ConnectionPresentation.Offline;
    }

    private void RefreshTodayRules()
    {
        OnPropertyChanged(nameof(ShowTodayRulesCard));
        OnPropertyChanged(nameof(TodayRulesTitle));
        OnPropertyChanged(nameof(ConnectionStateTitle));
        OnPropertyChanged(nameof(ConnectionStateBody));
        OnPropertyChanged(nameof(ConnectionStateIsLinked));
        OnPropertyChanged(nameof(ConnectionStateIsPending));
        OnPropertyChanged(nameof(ConnectionStateIsOffline));

        if (!ShowTodayRulesCard)
        {
            ScheduleCollectionUpdate(() => TodayRules.Clear());
            RefreshAppTimeLimitRules();
            return;
        }

        var lines = BuildTodayRuleLines();
        ScheduleCollectionUpdate(() => ReplaceTodayRuleLines(TodayRules, lines));

        RefreshAppTimeLimitRules();
    }

    private void RefreshAppTimeLimitRules()
    {
        OnPropertyChanged(nameof(ShowAppTimeLimitsCard));
        OnPropertyChanged(nameof(ShowAppTimeLimitsEmpty));
        OnPropertyChanged(nameof(AppTimeLimitsCardTitle));
        OnPropertyChanged(nameof(AppTimeLimitsCardSubtitle));
        OnPropertyChanged(nameof(AppTimeLimitsEmptyMessage));

        if (!ShowAppTimeLimitsCard)
        {
            ScheduleCollectionUpdate(() => AppTimeLimitRules.Clear());
            return;
        }

        var rows = _appTimeLimits.GetDisplayRows()
            .Select(app =>
            {
                var limitLabel = FormatDuration(TimeSpan.FromMinutes(app.LimitMinutes));
                var remainingLabel = FormatDuration(app.Remaining);
                return new TodayRuleLine
                {
                    Label = app.DisplayName,
                    IconGlyph = UiCopy.IconAppTimeLimit,
                    IconType = TodayRuleIconType.AppLimit,
                    Value = app.IsExhausted
                        ? UiCopy.TodayRulesAppExhaustedFormat(limitLabel)
                        : string.Format(UiCopy.TodayRulesAppRemainingFormat, remainingLabel, limitLabel),
                    IsEmphasis = app.IsExhausted,
                    Progress = app.Progress,
                };
            })
            .ToList();

        ScheduleCollectionUpdate(() => ReplaceTodayRuleLines(AppTimeLimitRules, rows));
    }

    private static void ReplaceTodayRuleLines(
        ObservableCollection<TodayRuleLine> target,
        IReadOnlyList<TodayRuleLine> source)
    {
        if (target.Count == source.Count)
        {
            var same = true;
            for (var i = 0; i < source.Count; i++)
            {
                if (!SameTodayRuleLine(target[i], source[i]))
                {
                    same = false;
                    break;
                }
            }

            if (same)
                return;
        }

        target.Clear();
        foreach (var line in source)
            target.Add(line);
    }

    private static bool SameTodayRuleLine(TodayRuleLine current, TodayRuleLine next) =>
        current.Label == next.Label
        && current.Value == next.Value
        && current.IconGlyph == next.IconGlyph
        && current.IconType == next.IconType
        && current.IsEmphasis == next.IsEmphasis
        && Nullable.Equals(current.Progress, next.Progress);

    private List<TodayRuleLine> BuildTodayRuleLines()
    {
        var lines = new List<TodayRuleLine>(6);

        var modeValue = IsDisciplineEscalated
            ? string.Format(UiCopy.TodayRulesModeEscalatedFormat, _agentMode.DisplayName, _agentMode.BaseDisplayName)
            : _agentMode.DisplayName;
        lines.Add(new TodayRuleLine
        {
            Label = UiCopy.TodayRulesModeLabel,
            IconGlyph = UiCopy.IconSupervisionMode,
            IconType = TodayRuleIconType.Supervision,
            Value = modeValue,
            IsEmphasis = IsDisciplineEscalated,
        });

        lines.Add(new TodayRuleLine
        {
            Label = UiCopy.TodayRulesScreenLabel,
            IconGlyph = UiCopy.IconScreenTime,
            IconType = TodayRuleIconType.ScreenTime,
            Value = ScreenTimeUsedMinutes >= ScreenTimeLimitMinutes
                ? UiCopy.TodayRulesScreenExhausted
                : string.Format(
                    UiCopy.TodayRulesScreenRemainingFormat,
                    FormatDuration(TimeSpan.FromMinutes(Math.Max(0, ScreenTimeLimitMinutes - ScreenTimeUsedMinutes))),
                    FormatDuration(TimeSpan.FromMinutes(ScreenTimeLimitMinutes))),
            IsEmphasis = ScreenTimeUsedMinutes >= ScreenTimeLimitMinutes,
        });

        lines.Add(new TodayRuleLine
        {
            Label = UiCopy.TodayRulesGamingLabel,
            IconGlyph = UiCopy.GameListIconGlyph,
            IconType = TodayRuleIconType.Gaming,
            Value = _gaming.LimitMinutes <= 0
                ? UiCopy.TodayRulesGamingZeroLimit
                : string.Format(
                    UiCopy.TodayRulesGamingRemainingFormat,
                    GamingRemainingLabel,
                    GamingLimitLabel),
            IsEmphasis = _gaming.LimitMinutes > 0 && _gaming.TotalUsage >= _gaming.LimitDuration,
        });

        lines.Add(new TodayRuleLine
        {
            Label = UiCopy.TodayRulesYoutubeLabel,
            IconGlyph = UiCopy.IconYoutube,
            IconType = TodayRuleIconType.YouTube,
            Value = _youtube.LimitMinutes <= 0
                ? UiCopy.TodayRulesYoutubeZeroLimit
                : string.Format(
                    UiCopy.TodayRulesYoutubeRemainingFormat,
                    FormatYoutubeRemainingLabel(),
                    YoutubeLimitLabel),
            IsEmphasis = _youtube.LimitMinutes > 0 && _youtube.TotalUsage >= _youtube.LimitDuration,
        });

        if (_studyTime.Settings.Enabled)
        {
            lines.Add(new TodayRuleLine
            {
                Label = UiCopy.TodayRulesStudyLabel,
                IconGlyph = UiCopy.IconStudy,
                IconType = TodayRuleIconType.Study,
                Value = IsStudyModeActive
                    ? (_studyTime.ActiveUntilLabel is { Length: > 0 } until
                        ? $"{UiCopy.StudyTimeTileActive} · {until}"
                        : UiCopy.StudyTimeTileActive)
                    : StudyTimeLabel,
                IsEmphasis = IsStudyModeActive,
            });
        }

        var bedtimeToday = _bedtime.Settings.Resolve(DateTime.Now);
        lines.Add(new TodayRuleLine
        {
            Label = UiCopy.TodayRulesBedtimeLabel,
            IconGlyph = UiCopy.IconBedtime,
            IconType = TodayRuleIconType.Bedtime,
            Value = bedtimeToday.Enabled ? BedtimeLabel : UiCopy.TodayRulesBedtimeOff,
            IsEmphasis = false,
        });

        return lines;
    }

    private string FormatYoutubeRemainingLabel()
    {
        var remaining = _youtube.LimitDuration - _youtube.TotalUsage;
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;
        return FormatDuration(remaining);
    }
}

