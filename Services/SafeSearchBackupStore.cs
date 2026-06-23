using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduGuardAgent.Services;

internal sealed class SafeSearchBackupStore
{
    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir,
        "safesearch");

    private static readonly string StatePath = Path.Combine(StateDir, "state.json");

    public SafeSearchState? Load()
    {
        if (!File.Exists(StatePath))
            return null;

        try
        {
            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<SafeSearchState>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(SafeSearchState state)
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

    public string BackupFilePath(string label, string fileName)
    {
        var dir = Path.Combine(StateDir, "files", label);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }
}

internal sealed class SafeSearchState
{
    [JsonPropertyName("registry")]
    public List<RegistryValueBackup> Registry { get; init; } = [];

    [JsonPropertyName("firefox_policies")]
    public List<FirefoxPolicyBackup> FirefoxPolicies { get; init; } = [];
}

internal sealed class RegistryValueBackup
{
    [JsonPropertyName("key_path")]
    public required string KeyPath { get; init; }

    [JsonPropertyName("value_name")]
    public required string ValueName { get; init; }

    [JsonPropertyName("had_value")]
    public bool HadValue { get; init; }

    [JsonPropertyName("previous_dword")]
    public int? PreviousDword { get; init; }
}

internal sealed class FirefoxPolicyBackup
{
    [JsonPropertyName("policy_path")]
    public required string PolicyPath { get; init; }

    [JsonPropertyName("had_file")]
    public bool HadFile { get; init; }

    [JsonPropertyName("backup_path")]
    public string? BackupPath { get; set; }
}
