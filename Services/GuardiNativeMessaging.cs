using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace EduGuardAgent.Services;

/// <summary>Headless stdio host for Firefox/Chrome native messaging from Guardi Image Shield.</summary>
[SupportedOSPlatform("windows")]
internal static class GuardiNativeMessaging
{
    public const string HostName = "com.guardi.eduguard";

    public static int Run()
    {
        try
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            RunLoop(stdin, stdout);
            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static void RunLoop(Stream stdin, Stream stdout)
    {
        var lengthBytes = new byte[4];
        while (ReadExact(stdin, lengthBytes, 4))
        {
            var length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= 0 || length > 1_048_576)
                break;

            var payload = new byte[length];
            if (!ReadExact(stdin, payload, length))
                break;

            var response = HandleMessage(payload);
            WriteMessage(stdout, response);
        }
    }

    private static byte[] HandleMessage(byte[] payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            if (string.Equals(type, ExtensionInfractionInbox.HeartbeatType, StringComparison.OrdinalIgnoreCase))
            {
                var browser = root.TryGetProperty("browser", out var b) ? b.GetString() : null;
                var extensionId = root.TryGetProperty("extensionId", out var id) ? id.GetString() : null;
                var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
                var shieldActive = ExtensionHeartbeatHub.ReadBoolProperty(root, "shieldActive");
                var modelReady = ExtensionHeartbeatHub.ReadBoolProperty(root, "modelReady");
                ExtensionHeartbeatHub.RecordFromPayload(browser, extensionId, version, shieldActive, modelReady);
            }
            else if (string.Equals(type, ExtensionInfractionInbox.BlockedSearchType, StringComparison.Ordinal))
            {
                var query = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
                var match = root.TryGetProperty("match", out var m) ? m.GetString() ?? "" : "";
                ExtensionInfractionInbox.EnqueueBlockedSearch(query, match);
            }

            return Encoding.UTF8.GetBytes("{\"ok\":true}");
        }
        catch
        {
            return Encoding.UTF8.GetBytes("{\"ok\":false}");
        }
    }

    private static void WriteMessage(Stream stdout, byte[] json)
    {
        var len = BitConverter.GetBytes(json.Length);
        stdout.Write(len, 0, 4);
        stdout.Write(json, 0, json.Length);
        stdout.Flush();
    }

    private static bool ReadExact(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
                return false;
            offset += read;
        }
        return true;
    }
}
