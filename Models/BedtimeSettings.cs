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
        var (enabled, bedtime, wake) = Resolve(moment);
        if (enabled && IsInLockWindow(TimeOnly.FromDateTime(moment), bedtime, wake))
            return true;

        // Overnight lock started yesterday may still apply this morning.
        var yesterday = moment.AddDays(-1);
        var (yEnabled, yBedtime, yWake) = Resolve(yesterday);
        if (!yEnabled || yBedtime <= yWake)
            return false;

        return TimeOnly.FromDateTime(moment) < yWake;
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
        var (enabled, bedtime, wake) = Resolve(now);
        if (!enabled)
            return now;

        return GetNextWakeAt(now, bedtime, wake);
    }

    public static TimeSpan TimeUntilWake(DateTime now, TimeOnly bedtime, TimeOnly wake)
    {
        var wakeAt = GetNextWakeAt(now, bedtime, wake);
        var remaining = wakeAt - now;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public TimeSpan TimeUntilWake(DateTime now)
    {
        var (enabled, bedtime, wake) = Resolve(now);
        if (!enabled)
            return TimeSpan.Zero;

        return TimeUntilWake(now, bedtime, wake);
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
