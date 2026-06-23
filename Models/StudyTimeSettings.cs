using System.Text.Json.Serialization;

namespace EduGuardAgent.Models;

internal sealed class StudyTimeSettings
{
    public bool Enabled { get; init; }
    public TimeOnly StartTime { get; init; } = new(9, 0);
    public TimeOnly EndTime { get; init; } = new(17, 0);
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];
    public IReadOnlyDictionary<DayOfWeek, StudyDayConfig> Weekly { get; init; } =
        new Dictionary<DayOfWeek, StudyDayConfig>();

    public bool BlockGames { get; init; } = true;
    public bool BlockYoutube { get; init; } = true;
    public bool BlockDistractingSites { get; init; } = true;
    public bool BlockDistractingApps { get; init; } = true;

    public static StudyTimeSettings Default => new()
    {
        Days = ParseDays(["mon", "tue", "wed", "thu", "fri"]),
    };

    public static IReadOnlyList<DayOfWeek> ParseDays(IEnumerable<string>? days)
    {
        if (days is null)
            return [];

        var result = new HashSet<DayOfWeek>();
        foreach (var day in days)
        {
            if (string.IsNullOrWhiteSpace(day))
                continue;

            if (DayScheduleKeys.TryParse(day.Trim(), out var dow))
                result.Add(dow);
        }

        return result.OrderBy(d => (int)d).ToArray();
    }

    public static IReadOnlyList<string> FormatDays(IEnumerable<DayOfWeek> days) =>
        days
            .Distinct()
            .OrderBy(d => (int)d)
            .Select(DayScheduleKeys.Format)
            .ToArray();

    public static StudyTimeSettings FromPayload(
        bool enabled,
        string? startTime,
        string? endTime,
        IReadOnlyList<string>? days,
        StudyTimeSettings? current = null,
        Dictionary<string, StudyDayPayload>? weekly = null,
        bool? blockGames = null,
        bool? blockYoutube = null,
        bool? blockDistractingSites = null,
        bool? blockDistractingApps = null)
    {
        var basis = current ?? Default;
        var start = BedtimeSettings.TryParseTime(startTime, out var parsedStart) ? parsedStart : basis.StartTime;
        var end = BedtimeSettings.TryParseTime(endTime, out var parsedEnd) ? parsedEnd : basis.EndTime;
        var parsedDays = days is not null ? ParseDays(days) : basis.Days;
        var mergedWeekly = weekly switch
        {
            null => basis.Weekly,
            { Count: 0 } => new Dictionary<DayOfWeek, StudyDayConfig>(),
            _ => MergeWeekly(basis.Weekly, weekly),
        };

        return new StudyTimeSettings
        {
            Enabled = enabled,
            StartTime = start,
            EndTime = end,
            Days = parsedDays,
            Weekly = mergedWeekly,
            BlockGames = blockGames ?? basis.BlockGames,
            BlockYoutube = blockYoutube ?? basis.BlockYoutube,
            BlockDistractingSites = blockDistractingSites ?? basis.BlockDistractingSites,
            BlockDistractingApps = blockDistractingApps ?? basis.BlockDistractingApps,
        };
    }

    private static Dictionary<DayOfWeek, StudyDayConfig> MergeWeekly(
        IReadOnlyDictionary<DayOfWeek, StudyDayConfig> current,
        Dictionary<string, StudyDayPayload> weekly)
    {
        var merged = new Dictionary<DayOfWeek, StudyDayConfig>(current);
        foreach (var (key, payload) in weekly)
        {
            if (!DayScheduleKeys.TryParse(key, out var day))
                continue;

            merged.TryGetValue(day, out var existing);
            merged[day] = new StudyDayConfig
            {
                Enabled = payload.Enabled ?? existing?.Enabled,
                StartTime = BedtimeSettings.TryParseTime(payload.StartTime, out var parsedStart)
                    ? parsedStart
                    : existing?.StartTime,
                EndTime = BedtimeSettings.TryParseTime(payload.EndTime, out var parsedEnd)
                    ? parsedEnd
                    : existing?.EndTime,
            };
        }

        return merged;
    }

    public (bool Enabled, TimeOnly StartTime, TimeOnly EndTime) Resolve(DateTime moment) =>
        Resolve(moment.DayOfWeek);

    public (bool Enabled, TimeOnly StartTime, TimeOnly EndTime) Resolve(DayOfWeek day)
    {
        if (Weekly.TryGetValue(day, out var config))
        {
            var dayEnabled = config.Enabled ?? Enabled;
            if (!dayEnabled)
                return (false, StartTime, EndTime);

            return (
                true,
                config.StartTime ?? StartTime,
                config.EndTime ?? EndTime);
        }

        if (!Enabled)
            return (false, StartTime, EndTime);

        if (Days.Count == 0 || !Days.Contains(day))
            return (false, StartTime, EndTime);

        return (true, StartTime, EndTime);
    }

    public bool HasSchedule =>
        Days.Count > 0 || Weekly.Count > 0;

    public string ScheduleSummary
    {
        get
        {
            if (!HasSchedule)
                return "Not scheduled";

            var dayLabel = Weekly.Count > 0 && Days.Count == 0
                ? "Custom weekly schedule"
                : Weekly.Count > 0
                    ? $"{FormatDayRange(Days)} + overrides"
                    : FormatDayRange(Days);
            var start = StartTime.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            var end = EndTime.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            return $"{dayLabel}, {start} – {end}";
        }
    }

    public static bool IsActive(DateTime moment, StudyTimeSettings settings)
    {
        var (enabled, start, end) = settings.Resolve(moment);
        if (!enabled)
            return false;

        return IsInWindow(TimeOnly.FromDateTime(moment), start, end);
    }

    public static bool IsInWindow(TimeOnly now, TimeOnly start, TimeOnly end)
    {
        if (start == end)
            return false;

        return start > end
            ? now >= start || now < end
            : now >= start && now < end;
    }

    public string ActiveUntilLabel(DateTime moment)
    {
        var (_, _, end) = Resolve(moment);
        return end.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string DisplayLabel
    {
        get
        {
            if (!HasSchedule)
                return "Not scheduled";

            if (!Enabled)
                return $"Paused — {ScheduleSummary}";

            return ScheduleSummary;
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
                               $"{(c.StartTime?.ToString("HH:mm") ?? "-")}:" +
                               $"{(c.EndTime?.ToString("HH:mm") ?? "-")}";
                    }));

            return $"{Enabled}|{StartTime:HH:mm}|{EndTime:HH:mm}|" +
                   $"{string.Join(",", FormatDays(Days))}|{weekly}|" +
                   $"{BlockGames}|{BlockYoutube}|{BlockDistractingSites}|{BlockDistractingApps}";
        }
    }

    private static string FormatDayRange(IReadOnlyList<DayOfWeek> days)
    {
        if (days.Count == 7)
            return "Every day";

        if (days.Count == 5
            && days.Contains(DayOfWeek.Monday)
            && days.Contains(DayOfWeek.Tuesday)
            && days.Contains(DayOfWeek.Wednesday)
            && days.Contains(DayOfWeek.Thursday)
            && days.Contains(DayOfWeek.Friday)
            && !days.Contains(DayOfWeek.Saturday)
            && !days.Contains(DayOfWeek.Sunday))
        {
            return "Mon–Fri";
        }

        return string.Join(", ", days.Select(d => d.ToString()[..3]));
    }
}

internal sealed class StudyDayConfig
{
    public bool? Enabled { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
}

internal sealed class StudyDayPayload
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; init; }
}

internal sealed class StudyTimeSettingsPayload
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("start_time")]
    public string? StartTime { get; init; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; init; }

    [JsonPropertyName("days")]
    public List<string>? Days { get; init; }

    [JsonPropertyName("weekly")]
    public Dictionary<string, StudyDayPayload>? Weekly { get; init; }

    [JsonPropertyName("block_games")]
    public bool? BlockGames { get; init; }

    [JsonPropertyName("block_youtube")]
    public bool? BlockYoutube { get; init; }

    [JsonPropertyName("block_distracting_sites")]
    public bool? BlockDistractingSites { get; init; }

    [JsonPropertyName("block_distracting_apps")]
    public bool? BlockDistractingApps { get; init; }
}
