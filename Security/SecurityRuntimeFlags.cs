using System.Text.Json;
using System.Text.Json.Serialization;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

/// <summary>
/// Persists effective security flags for cross-process readers (e.g. the watchdog).
/// </summary>
internal static class SecurityRuntimeFlags
{
    private static readonly string FlagsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "security_runtime.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static volatile bool _cachedBlockProcessKillers = true;

    public static void Persist(ModeFeatures features)
    {
        _cachedBlockProcessKillers = features.BlockProcessKillers;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FlagsPath)!);
            var payload = new StoredFlags { BlockProcessKillers = features.BlockProcessKillers };
            File.WriteAllText(FlagsPath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch
        {
            // Best-effort.
        }
    }

    public static bool ShouldBlockProcessKillers()
    {
        try
        {
            if (!File.Exists(FlagsPath))
                return _cachedBlockProcessKillers;

            var json = File.ReadAllText(FlagsPath);
            var stored = JsonSerializer.Deserialize<StoredFlags>(json, JsonOptions);
            if (stored is not null)
                _cachedBlockProcessKillers = stored.BlockProcessKillers;

            return _cachedBlockProcessKillers;
        }
        catch
        {
            return _cachedBlockProcessKillers;
        }
    }

    /// <summary>Loads persisted flags from disk before the watchdog thread starts.</summary>
    public static void EnsureLoadedFromDisk() => _ = ShouldBlockProcessKillers();

    private sealed class StoredFlags
    {
        [JsonPropertyName("blockProcessKillers")]
        public bool BlockProcessKillers { get; init; }
    }
}
