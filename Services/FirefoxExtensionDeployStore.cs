using System.Text.Json;
using System.Text.Json.Serialization;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Tracks the last store-config version Guardi deployed to Firefox via enterprise policy.
/// Used to restart Firefox once per XPI release even when install_url was already updated.
/// </summary>
internal static class FirefoxExtensionDeployStore
{
    private static readonly object Gate = new();
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "firefox-extension-deploy.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static string? LastDeployedVersion
    {
        get
        {
            lock (Gate)
                return Load().Version;
        }
    }

    public static bool NeedsDeploy(string targetVersion)
    {
        if (string.IsNullOrWhiteSpace(targetVersion))
            return false;

        lock (Gate)
        {
            var current = Load().Version;
            return !string.Equals(current, targetVersion, StringComparison.Ordinal);
        }
    }

    public static void MarkDeployed(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return;

        lock (Gate)
        {
            Save(new DeployState(version, DateTime.UtcNow));
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(StatePath))
                    File.Delete(StatePath);
            }
            catch (Exception ex)
            {
                AuditLog.Write($"Firefox extension deploy state clear failed: {ex.Message}");
            }
        }
    }

    private static DeployState Load()
    {
        try
        {
            if (!File.Exists(StatePath))
                return DeployState.Empty;

            var json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<DeployState>(json, JsonOptions) ?? DeployState.Empty;
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Firefox extension deploy state load failed: {ex.Message}");
            return DeployState.Empty;
        }
    }

    private static void Save(DeployState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Firefox extension deploy state save failed: {ex.Message}");
        }
    }

    private sealed record DeployState(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("deployed_at_utc")] DateTime DeployedAtUtc)
    {
        public static DeployState Empty { get; } = new(null, DateTime.MinValue);
    }
}
