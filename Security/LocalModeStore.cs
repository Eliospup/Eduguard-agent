using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class LocalModeStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "local_mode.json");

    public StoredLocalMode Load()
    {
        if (!File.Exists(SettingsPath))
            return StoredLocalMode.Default;

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<StoredLocalMode>(json) ?? StoredLocalMode.Default;
        }
        catch
        {
            return StoredLocalMode.Default;
        }
    }

    public void Save(StoredLocalMode stored)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(stored));
    }

    internal sealed class StoredLocalMode
    {
        public bool Enabled { get; init; }
        public DateTimeOffset? EnabledAt { get; init; }

        public static StoredLocalMode Default => new();
    }
}
