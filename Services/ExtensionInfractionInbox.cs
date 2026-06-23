using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduGuardAgent.Services;

/// <summary>Queue for infraction events written by the browser extension native host.</summary>
internal static class ExtensionInfractionInbox
{
    internal const string BlockedSearchType = "blocked_search";
    internal const string HeartbeatType = "heartbeat";

    private static readonly string InboxDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir,
        "inbox");

    public static string DirectoryPath => InboxDir;

    public static void EnqueueBlockedSearch(string query, string matchLabel)
    {
        Directory.CreateDirectory(InboxDir);
        var payload = new ExtensionInfractionEvent
        {
            Type = BlockedSearchType,
            Query = Truncate(query, 160),
            Match = Truncate(matchLabel, 80),
            At = DateTimeOffset.UtcNow.ToString("o"),
        };
        var path = Path.Combine(InboxDir, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload));
    }

    public static ExtensionInfractionEvent? TryRead(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ExtensionInfractionEvent>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

internal sealed class ExtensionInfractionEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("match")]
    public string? Match { get; init; }

    [JsonPropertyName("at")]
    public string? At { get; init; }
}
