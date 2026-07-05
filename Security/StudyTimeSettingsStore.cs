using System.Text.Json;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

internal sealed class StudyTimeSettingsStore
{
    private const string FileName = "study_time_settings.json";

    public StudyTimeSettings Load()
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredStudyTimeSettings>(json);
                if (stored is null)
                    return StudyTimeSettings.Default;

                return StudyTimeSettings.FromPayload(
                    stored.Enabled,
                    stored.StartTime,
                    stored.EndTime,
                    stored.Days,
                    weekly: stored.Weekly?.ToDictionary(
                        p => p.Key,
                        p => new StudyDayPayload
                        {
                            Enabled = p.Value.Enabled,
                            StartTime = p.Value.StartTime,
                            EndTime = p.Value.EndTime,
                        },
                        StringComparer.OrdinalIgnoreCase),
                    blockGames: stored.BlockGames,
                    blockYoutube: stored.BlockYoutube,
                    blockDistractingSites: stored.BlockDistractingSites,
                    blockDistractingApps: stored.BlockDistractingApps);
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
            AuditLog.Write("SECURITY: study-time settings failed integrity check — using defaults (re-driven from the secured catalog).");

        return StudyTimeSettings.Default;
    }

    public void Save(StudyTimeSettings settings)
    {
        var stored = new StoredStudyTimeSettings
        {
            Enabled = settings.Enabled,
            StartTime = settings.StartTime.ToString("HH:mm"),
            EndTime = settings.EndTime.ToString("HH:mm"),
            Days = StudyTimeSettings.FormatDays(settings.Days).ToList(),
            Weekly = settings.Weekly.Count == 0
                ? null
                : settings.Weekly.ToDictionary(
                    p => DayScheduleKeys.Format(p.Key),
                    p => new StoredStudyDay
                    {
                        Enabled = p.Value.Enabled,
                        StartTime = p.Value.StartTime?.ToString("HH:mm"),
                        EndTime = p.Value.EndTime?.ToString("HH:mm"),
                    },
                    StringComparer.OrdinalIgnoreCase),
            BlockGames = settings.BlockGames,
            BlockYoutube = settings.BlockYoutube,
            BlockDistractingSites = settings.BlockDistractingSites,
            BlockDistractingApps = settings.BlockDistractingApps,
        };

        SecureStateFile.Write(FileName, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class StoredStudyTimeSettings
    {
        public bool Enabled { get; init; }
        public string? StartTime { get; init; }
        public string? EndTime { get; init; }
        public List<string>? Days { get; init; }
        public Dictionary<string, StoredStudyDay>? Weekly { get; init; }
        public bool BlockGames { get; init; } = true;
        public bool BlockYoutube { get; init; } = true;
        public bool BlockDistractingSites { get; init; } = true;
        public bool BlockDistractingApps { get; init; } = true;
    }

    private sealed class StoredStudyDay
    {
        public bool? Enabled { get; init; }
        public string? StartTime { get; init; }
        public string? EndTime { get; init; }
    }
}
