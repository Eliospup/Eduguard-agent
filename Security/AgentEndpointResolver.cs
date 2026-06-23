using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduGuardAgent.Security;

/// <summary>
/// Resolves the Lovable dashboard URL. Release builds default to production;
/// Debug builds default to the -dev preview. Optional overrides via
/// EDUGUARD_BASE_URL or %AppData%\EduGuard\endpoint.json (staging only in Release).
/// </summary>
internal static class AgentEndpointResolver
{
    private const string ProdDefault =
        "https://project--fc13ed6d-bdd2-4df8-af88-c8d4c6c2f9db.lovable.app";

    private const string DevDefault =
        "https://project--fc13ed6d-bdd2-4df8-af88-c8d4c6c2f9db-dev.lovable.app";

    private static readonly string EndpointFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "endpoint.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Resolve()
    {
        var fromEnv = Normalize(Environment.GetEnvironmentVariable("EDUGUARD_BASE_URL"));
        if (fromEnv is not null && AcceptOverride(fromEnv, "EDUGUARD_BASE_URL"))
            return fromEnv;

        var fromFile = TryReadFile();
        if (fromFile is not null && AcceptOverride(fromFile, EndpointFilePath))
            return fromFile;

#if DEBUG
        return DevDefault;
#else
        return ProdDefault;
#endif
    }

    private static string? TryReadFile()
    {
        try
        {
            if (!File.Exists(EndpointFilePath))
                return null;

            var json = File.ReadAllText(EndpointFilePath);
            var payload = JsonSerializer.Deserialize<EndpointFile>(json, JsonOptions);
            return Normalize(payload?.BaseUrl);
        }
        catch
        {
            return null;
        }
    }

    private static bool AcceptOverride(string url, string source)
    {
#if DEBUG
        return true;
#else
        if (!url.Contains("-dev.lovable.app", StringComparison.OrdinalIgnoreCase))
            return true;

        AuditLog.Write(
            $"Ignored {source}: Release builds cannot target a -dev Lovable host ({url}).");
        return false;
#endif
    }

    private static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var trimmed = url.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
        {
            return null;
        }

        return trimmed;
    }

    private sealed class EndpointFile
    {
        [JsonPropertyName("baseUrl")]
        public string? BaseUrl { get; init; }
    }
}
