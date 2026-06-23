using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduGuardAgent.Services;

/// <summary>Persists what the image-shield extension policy changed so it can be cleanly reverted.</summary>
internal sealed class ImageShieldBackupStore
{
    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir,
        "imageshield");

    private static readonly string StatePath = Path.Combine(StateDir, "state.json");

    public ImageShieldState? Load()
    {
        if (!File.Exists(StatePath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ImageShieldState>(File.ReadAllText(StatePath));
        }
        catch
        {
            return null;
        }
    }

    public void Save(ImageShieldState state)
    {
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Clear()
    {
        if (File.Exists(StatePath))
            File.Delete(StatePath);
    }
}

internal sealed class ImageShieldState
{
    [JsonPropertyName("registry_values")]
    public List<RegistryStringBackup> RegistryValues { get; init; } = [];

    [JsonPropertyName("registry_keys_created")]
    public List<string> RegistryKeysCreated { get; init; } = [];

    [JsonPropertyName("firefox_addon_ids")]
    public List<string> FirefoxAddonIds { get; init; } = [];

    [JsonPropertyName("firefox_bundled_xpi_paths")]
    public List<string> FirefoxBundledXpiPaths { get; init; } = [];

    [JsonPropertyName("firefox_profile_unpacked_dirs")]
    public List<string> FirefoxProfileUnpackedDirs { get; init; } = [];

    [JsonPropertyName("chromium_profile_unpacked_dirs")]
    public List<string> ChromiumProfileUnpackedDirs { get; init; } = [];
}

internal sealed class RegistryStringBackup
{
    [JsonPropertyName("hive")]
    public required string Hive { get; init; }

    [JsonPropertyName("key_path")]
    public required string KeyPath { get; init; }

    [JsonPropertyName("value_name")]
    public required string ValueName { get; init; }

    [JsonPropertyName("had_value")]
    public bool HadValue { get; init; }

    [JsonPropertyName("previous_value")]
    public string? PreviousValue { get; init; }
}
