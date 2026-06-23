using System.Text.Json;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Security;

internal sealed class AgentModeStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "agent_mode.json");

    public StoredAgentMode Load()
    {
        if (!File.Exists(SettingsPath))
            return StoredAgentMode.Default;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<StoredAgentMode>(json) ?? StoredAgentMode.Default;
        }
        catch
        {
            return StoredAgentMode.Default;
        }
    }

    public void Save(StoredAgentMode stored)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored));
    }

    internal sealed class StoredAgentMode
    {
        public string Slug { get; init; } = AgentModeSlugs.TrustedSub;
        public string? DisplayName { get; init; }
        public int ScreenTimeLimitMinutes { get; init; } = AgentModeRegistry.TrustedSub.Defaults.ScreenTimeLimitMinutes;
        public Dictionary<string, int>? ScreenTimeWeeklyLimits { get; init; }
        public ModeFeatures Features { get; init; } = ModeFeatures.ForTrustedSub;

        public static StoredAgentMode Default => new();
    }
}
