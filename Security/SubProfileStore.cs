using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class SubProfileStore
{
    private static readonly string ProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        Config.SubProfileFileName);

    public StoredSubProfile Load()
    {
        if (!File.Exists(ProfilePath))
            return StoredSubProfile.Empty;

        try
        {
            var json = File.ReadAllText(ProfilePath);
            return JsonSerializer.Deserialize<StoredSubProfile>(json) ?? StoredSubProfile.Empty;
        }
        catch
        {
            return StoredSubProfile.Empty;
        }
    }

    public void Save(StoredSubProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
        File.WriteAllText(ProfilePath, JsonSerializer.Serialize(profile));
    }

    internal sealed class StoredSubProfile
    {
        public string? DisplayName { get; init; }

        public static StoredSubProfile Empty => new();
    }
}
