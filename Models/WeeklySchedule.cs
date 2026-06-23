using System.Collections.Frozen;
using System.Text.Json.Serialization;

namespace EduGuardAgent.Models;

/// <summary>Shared day-of-week keys for per-day schedules (same as study_time).</summary>
internal static class DayScheduleKeys
{
    private static readonly Dictionary<string, DayOfWeek> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sun"] = DayOfWeek.Sunday,
        ["mon"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
    };

    public static bool TryParse(string? key, out DayOfWeek day)
    {
        day = default;
        return !string.IsNullOrWhiteSpace(key) && Map.TryGetValue(key.Trim(), out day);
    }

    public static string Format(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "sun",
        DayOfWeek.Monday => "mon",
        DayOfWeek.Tuesday => "tue",
        DayOfWeek.Wednesday => "wed",
        DayOfWeek.Thursday => "thu",
        DayOfWeek.Friday => "fri",
        DayOfWeek.Saturday => "sat",
        _ => "mon",
    };
}

/// <summary>Daily minute cap with optional per-day overrides (mon–sun keys).</summary>
internal sealed class WeeklyMinuteLimits
{
    public int DefaultMinutes { get; init; }
    public IReadOnlyDictionary<DayOfWeek, int> DayMinutes { get; init; } = FrozenDictionary<DayOfWeek, int>.Empty;

    public int ForDay(DayOfWeek day) =>
        DayMinutes.TryGetValue(day, out var minutes) ? minutes : DefaultMinutes;

    public int ForDate(DateOnly date) => ForDay(date.DayOfWeek);

    public bool HasOverrides => DayMinutes.Count > 0;

    public string ScheduleKey =>
        $"{DefaultMinutes}|{string.Join(",", DayMinutes.OrderBy(p => (int)p.Key).Select(p => $"{DayScheduleKeys.Format(p.Key)}:{p.Value}"))}";

    public static WeeklyMinuteLimits Create(int defaultMinutes, IReadOnlyDictionary<DayOfWeek, int>? dayMinutes = null) =>
        new()
        {
            DefaultMinutes = Math.Clamp(defaultMinutes, 1, 1440),
            DayMinutes = dayMinutes is null || dayMinutes.Count == 0
                ? FrozenDictionary<DayOfWeek, int>.Empty
                : dayMinutes.ToFrozenDictionary(),
        };

    public static WeeklyMinuteLimits Parse(
        int? defaultMinutes,
        Dictionary<string, int>? weekly,
        WeeklyMinuteLimits? current,
        int fallbackDefault)
    {
        var basis = current ?? Create(fallbackDefault);
        var mergedDefault = defaultMinutes is { } d
            ? Math.Clamp(d, 1, 1440)
            : basis.DefaultMinutes;

        if (weekly is null)
            return Create(mergedDefault, basis.DayMinutes);

        var days = new Dictionary<DayOfWeek, int>(basis.DayMinutes);
        foreach (var (key, minutes) in weekly)
        {
            if (!DayScheduleKeys.TryParse(key, out var day))
                continue;

            days[day] = Math.Clamp(minutes, 1, 1440);
        }

        return Create(mergedDefault, days);
    }

    public static Dictionary<string, int> SerializeDays(IReadOnlyDictionary<DayOfWeek, int> days) =>
        days.ToDictionary(p => DayScheduleKeys.Format(p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase);
}

internal sealed class BedtimeDayPayload
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("time")]
    public string? Time { get; init; }

    [JsonPropertyName("wake_time")]
    public string? WakeTime { get; init; }
}

internal sealed class BedtimeDayConfig
{
    public bool? Enabled { get; init; }
    public TimeOnly? Time { get; init; }
    public TimeOnly? WakeTime { get; init; }
}
