using System.Text.Json;

using EduGuardAgent.Models;



namespace EduGuardAgent.Security;



internal sealed class BedtimeSettingsStore

{

    private static readonly string SettingsPath = Path.Combine(

        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),

        Config.AgentDataDir,

        "bedtime_settings.json");



    private static readonly string DailyStatePath = Path.Combine(

        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),

        Config.AgentDataDir,

        "bedtime_daily_state.json");



    public BedtimeSettings Load()

    {

        if (!File.Exists(SettingsPath))

            return BedtimeSettings.Default;



        try

        {

            var json = File.ReadAllText(SettingsPath);

            var stored = JsonSerializer.Deserialize<StoredBedtimeSettings>(json);

            if (stored is null)

                return BedtimeSettings.Default;



            return BedtimeSettings.FromPayload(
                stored.Enabled,
                stored.Time,
                stored.WakeTime,
                weekly: stored.Weekly?.ToDictionary(
                    p => p.Key,
                    p => new BedtimeDayPayload
                    {
                        Enabled = p.Value.Enabled,
                        Time = p.Value.Time,
                        WakeTime = p.Value.WakeTime,
                    },
                    StringComparer.OrdinalIgnoreCase),
                blueLightFilterEnabled: stored.BlueLightFilterEnabled);

        }

        catch

        {

            return BedtimeSettings.Default;

        }

    }



    public void Save(BedtimeSettings settings)

    {

        var dir = Path.GetDirectoryName(SettingsPath)!;

        Directory.CreateDirectory(dir);



        var stored = new StoredBedtimeSettings

        {

            Enabled = settings.Enabled,

            BlueLightFilterEnabled = settings.BlueLightFilterEnabled,

            Time = settings.Time.ToString("HH:mm"),

            WakeTime = settings.WakeTime.ToString("HH:mm"),

            Weekly = settings.Weekly.Count == 0

                ? null

                : settings.Weekly.ToDictionary(

                    p => DayScheduleKeys.Format(p.Key),

                    p => new StoredBedtimeDay

                    {

                        Enabled = p.Value.Enabled,

                        Time = p.Value.Time?.ToString("HH:mm"),

                        WakeTime = p.Value.WakeTime?.ToString("HH:mm"),

                    },

                    StringComparer.OrdinalIgnoreCase),

        };



        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(SettingsPath, json);

    }



    public BedtimeDailyState LoadDailyState(DateOnly today, string scheduleKey)

    {

        if (!File.Exists(DailyStatePath))

            return BedtimeDailyState.Empty(today, scheduleKey);



        try

        {

            var json = File.ReadAllText(DailyStatePath);

            var stored = JsonSerializer.Deserialize<StoredDailyState>(json);

            if (stored is null

                || stored.Date != today.ToString("yyyy-MM-dd")

                || stored.ScheduleKey != scheduleKey)

            {

                return BedtimeDailyState.Empty(today, scheduleKey);

            }



            return new BedtimeDailyState

            {

                Date = today,

                ScheduleKey = scheduleKey,

                WarnedOneHour = stored.WarnedOneHour,

                WarnedThirtyMinutes = stored.WarnedThirtyMinutes,

                WarnedFiveMinutes = stored.WarnedFiveMinutes,

                BedtimeTriggered = stored.BedtimeTriggered,

            };

        }

        catch

        {

            return BedtimeDailyState.Empty(today, scheduleKey);

        }

    }



    public void SaveDailyState(BedtimeDailyState state)

    {

        var dir = Path.GetDirectoryName(DailyStatePath)!;

        Directory.CreateDirectory(dir);



        var stored = new StoredDailyState

        {

            Date = state.Date.ToString("yyyy-MM-dd"),

            ScheduleKey = state.ScheduleKey,

            WarnedOneHour = state.WarnedOneHour,

            WarnedThirtyMinutes = state.WarnedThirtyMinutes,

            WarnedFiveMinutes = state.WarnedFiveMinutes,

            BedtimeTriggered = state.BedtimeTriggered,

        };



        var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(DailyStatePath, json);

    }



    public void ClearDailyState()

    {

        if (File.Exists(DailyStatePath))

            File.Delete(DailyStatePath);

    }



    private sealed class StoredBedtimeSettings

    {

        public bool Enabled { get; init; }

        public bool BlueLightFilterEnabled { get; init; } = true;

        public string? Time { get; init; }

        public string? WakeTime { get; init; }

        public Dictionary<string, StoredBedtimeDay>? Weekly { get; init; }

    }



    private sealed class StoredBedtimeDay

    {

        public bool? Enabled { get; init; }

        public string? Time { get; init; }

        public string? WakeTime { get; init; }

    }



    private sealed class StoredDailyState

    {

        public string? Date { get; init; }

        public string? ScheduleKey { get; init; }

        public bool WarnedOneHour { get; init; }

        public bool WarnedThirtyMinutes { get; init; }

        public bool WarnedFiveMinutes { get; init; }

        public bool BedtimeTriggered { get; init; }

    }

}



internal sealed record BedtimeDailyState

{

    public required DateOnly Date { get; init; }

    public required string ScheduleKey { get; init; }

    public bool WarnedOneHour { get; init; }

    public bool WarnedThirtyMinutes { get; init; }

    public bool WarnedFiveMinutes { get; init; }

    public bool BedtimeTriggered { get; init; }



    public static BedtimeDailyState Empty(DateOnly date, string scheduleKey) =>

        new()

        {

            Date = date,

            ScheduleKey = scheduleKey,

        };



    public BedtimeDailyState WithWarning(BedtimeWarningKind kind) => kind switch

    {

        BedtimeWarningKind.OneHour => this with { WarnedOneHour = true },

        BedtimeWarningKind.ThirtyMinutes => this with { WarnedThirtyMinutes = true },

        BedtimeWarningKind.FiveMinutes => this with { WarnedFiveMinutes = true },

        _ => this,

    };



    public BedtimeDailyState WithBedtimeTriggered() =>

        this with { BedtimeTriggered = true };

}

