using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduGuardAgent.Services;

internal sealed class YouTubeRestrictedModeBackupStore
{
    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir,
        "youtuberestrict");

    private static readonly string StatePath = Path.Combine(StateDir, "state.json");

    public YouTubeRestrictedModeState? Load()
    {
        if (!File.Exists(StatePath))
            return null;

        try
        {
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<YouTubeRestrictedModeState>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(YouTubeRestrictedModeState state)
    {
        Directory.CreateDirectory(StateDir);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StatePath, json);
    }

    public void Clear()
    {
        if (File.Exists(StatePath))
            File.Delete(StatePath);
    }
}

internal sealed class YouTubeRestrictedModeState
{
    [JsonPropertyName("registry")]
    public List<RegistryValueBackup> Registry { get; init; } = [];
}
