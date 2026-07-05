namespace EduGuardAgent.Models;

internal sealed class BedtimeSettings
{
    public bool Enabled { get; init; } = true;
    public bool BlueLightFilterEnabled { get; init; } = true;
    public TimeOnly Time { get; init; } = new(23, 0);
    public TimeOnly WakeTime { get; init; } = new(7, 0);
    public IReadOnlyDictionary<DayOfWeek, BedtimeDayConfig> Weekly { get; init; } =
        new Dictionary<DayOfWeek, BedtimeDayConfig>();

    public static BedtimeSettings Default => new();

    public static bool TryParseTime(string? value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return TimeOnly.TryParse(value.Trim(), out time)
            || TimeOnly.TryParseExact(value.Trim(), "HH:mm", out time)
            || TimeOnly.TryParseExact(value.Trim(), "H:mm", out time);
    }

    public static BedtimeSettings FromPayload(
        bool enabled,
        string? time,
        string? wakeTime,
        BedtimeSettings? current = null,
        Dictionary<string, BedtimeDayPayload>? weekly = null,
        bool? blueLightFilterEnabled = null)
    {
        var basis = current ?? Default;
        var bedtime = TryParseTime(time, out var parsedBedtime) ? parsedBedtime : basis.Time;
        var wake = TryParseTime(wakeTime, out var parsedWake) ? parsedWake : basis.WakeTime;
        var mergedWeekly = weekly is null
            ? basis.Weekly
            : MergeWeekly(basis.Weekly, weekly);

        return new BedtimeSettings
        {
            Enabled = enabled,
            BlueLightFilterEnabled = blueLightFilterEnabled ?? basis.BlueLightFilterEnabled,
            Time = bedtime,
            WakeTime = wake,
            Weekly = mergedWeekly,
        };
    }

    private static Dictionary<DayOfWeek, BedtimeDayConfig> MergeWeekly(
        IReadOnlyDictionary<DayOfWeek, BedtimeDayConfig> current,
        Dictionary<string, BedtimeDayPayload> weekly)
    {
        var merged = new Dictionary<DayOfWeek, BedtimeDayConfig>(current);
        foreach (var (key, payload) in weekly)
        {
            if (!DayScheduleKeys.TryParse(key, out var day))
                continue;

            merged.TryGetValue(day, out var existing);
            merged[day] = new BedtimeDayConfig
            {
                Enabled = payload.Enabled ?? existing?.Enabled,
                Time = TryParseTime(payload.Time, out var parsedTime) ? parsedTime : existing?.Time,
                WakeTime = TryParseTime(payload.WakeTime, out var parsedWake) ? parsedWake : existing?.WakeTime,
            };
        }

        return merged;
    }

    public (bool Enabled, TimeOnly Time, TimeOnly WakeTime) Resolve(DateTime moment) =>
        Resolve(moment.DayOfWeek);

    public (bool Enabled, TimeOnly Time, TimeOnly WakeTime) Resolve(DayOfWeek day)
    {
        if (Weekly.TryGetValue(day, out var config))
        {
            return (
                config.Enabled ?? Enabled,
                config.Time ?? Time,
                config.WakeTime ?? WakeTime);
        }

        return (Enabled, Time, WakeTime);
    }

    public bool IsInLockWindow(DateTime moment)
    {
        var now = TimeOnly.FromDateTime(moment);

        // Today's window, counted from where it STARTS today:
        //  • same-day window (bedtime < wake): locked while bedtime <= now < wake;
        //  • overnight window (bedtime > wake): only the EVENING part starts today (now >= bedtime).
        //    Its morning tail belongs to the next calendar day and is handled by the yesterday
        //    check below — attributing it to today (now < today's wake) is what made a day with an
        //    override (e.g. "off" yesterday, or a different wake time) lock at the wrong moment.
        var (enabled, bedtime, wake) = Resolve(moment);
        if (enabled && bedtime != wake)
        {
            if (bedtime < wake)
            {
                if (now >= bedtime && now < wake)
                    return true;
            }
            else if (now >= bedtime)
            {
                return true;
            }
        }

        // Morning carryover: an overnight lock that started YESTERDAY is still active until
        // yesterday's wake this morning. Using yesterday's own wake (not today's) is what makes
        // per-day overrides with different wake times release at the right time.
        var (yEnabled, yBedtime, yWake) = Resolve(moment.AddDays(-1));
        return yEnabled && yBedtime > yWake && now < yWake;
    }

    public string DisplayLabel
    {
        get
        {
            if (Weekly.Count == 0)
            {
                return $"{FormatTime(Time)} → {FormatTime(WakeTime)}";
            }

            var today = Resolve(DateTime.Now);
            if (!today.Enabled)
                return "Off today";

            return $"{FormatTime(today.Time)} → {FormatTime(today.WakeTime)} (today)";
        }
    }

    public string CardDisplayLabel
    {
        get
        {
            if (Weekly.Count == 0)
            {
                return $"{FormatTime(Time)}\n→ {FormatTime(WakeTime)}";
            }

            var today = Resolve(DateTime.Now);
            if (!today.Enabled)
                return "Off today";

            return $"{FormatTime(today.Time)}\n→ {FormatTime(today.WakeTime)}";
        }
    }

    public string ScheduleKey
    {
        get
        {
            var weekly = Weekly.Count == 0
                ? string.Empty
                : string.Join(",", Weekly
                    .OrderBy(p => (int)p.Key)
                    .Select(p =>
                    {
                        var c = p.Value;
                        return $"{DayScheduleKeys.Format(p.Key)}:" +
                               $"{(c.Enabled.HasValue ? c.Enabled.Value ? 1 : 0 : -1)}:" +
                               $"{(c.Time?.ToString("HH:mm") ?? "-")}:" +
                               $"{(c.WakeTime?.ToString("HH:mm") ?? "-")}";
                    }));

            return $"{Enabled}|{Time:HH:mm}|{WakeTime:HH:mm}|{weekly}";
        }
    }

    public string TodayScheduleKey(DateTime moment)
    {
        var (enabled, bedtime, wake) = Resolve(moment);
        var day = DateOnly.FromDateTime(moment);
        return $"{day:yyyy-MM-dd}|{enabled}|{bedtime:HH:mm}|{wake:HH:mm}";
    }

    public static bool IsInLockWindow(TimeOnly now, TimeOnly bedtime, TimeOnly wake)
    {
        if (bedtime == wake)
            return false;

        return bedtime > wake
            ? now >= bedtime || now < wake
            : now >= bedtime && now < wake;
    }

    public static DateTime GetNextWakeAt(DateTime now, TimeOnly bedtime, TimeOnly wake)
    {
        var today = DateOnly.FromDateTime(now);
        var wakeToday = today.ToDateTime(wake);

        if (bedtime > wake)
        {
            if (now < wakeToday)
                return wakeToday;

            return today.AddDays(1).ToDateTime(wake);
        }

        if (now < wakeToday)
            return wakeToday;

        return today.AddDays(1).ToDateTime(wake);
    }

    public DateTime GetNextWakeAt(DateTime now)
    {
        var nowT = TimeOnly.FromDateTime(now);
        var today = DateOnly.FromDateTime(now);

        // Morning carryover from yesterday's overnight lock → unlocks at yesterday's wake, today.
        var (yEnabled, yBedtime, yWake) = Resolve(now.AddDays(-1));
        if (yEnabled && yBedtime > yWake && nowT < yWake)
            return today.ToDateTime(yWake);

        var (enabled, bedtime, wake) = Resolve(now);
        if (!enabled || bedtime == wake)
            return now;

        if (bedtime < wake)
        {
            // Same-day window: unlocks at wake today (only meaningful while inside it).
            return nowT >= bedtime && nowT < wake ? today.ToDateTime(wake) : now;
        }

        // Overnight window: the evening part locks until tomorrow's wake.
        return nowT >= bedtime ? today.AddDays(1).ToDateTime(wake) : now;
    }

    public static TimeSpan TimeUntilWake(DateTime now, TimeOnly bedtime, TimeOnly wake)
    {
        var wakeAt = GetNextWakeAt(now, bedtime, wake);
        var remaining = wakeAt - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public TimeSpan TimeUntilWake(DateTime now)
    {
        var wakeAt = GetNextWakeAt(now);
        var remaining = wakeAt - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private static string FormatTime(TimeOnly time) =>
        time.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
}

internal enum BedtimeWarningKind
{
    OneHour,
    ThirtyMinutes,
    FiveMinutes,
}

internal enum BlueLightFilterPhase
{
    Off,
    Early,
    Late,
    Lock,
}
