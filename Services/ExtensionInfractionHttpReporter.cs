using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Local loopback bridge used by the browser extension while Guardi is running.
/// A raw TcpListener avoids Windows HttpListener URL ACL failures.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ExtensionInfractionHttpReporter : IDisposable
{
    public const int Port = 38473;
    public const string BlockedSearchPath = "/blocked-search";
    public const string ShieldStatePath = "/shield-state";
    public const string HeartbeatPath = "/extension-heartbeat";
    public const string YoutubeSoftAckPath = "/youtube-soft-ack";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly Action<string, string> _onBlockedSearch;
    private readonly Func<AgentShieldStateDto>? _getShieldState;
    private readonly Action? _onYoutubeSoftAck;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;

    public ExtensionInfractionHttpReporter(
        Action<string, string> onBlockedSearch,
        Func<AgentShieldStateDto>? getShieldState = null,
        Action? onYoutubeSoftAck = null)
    {
        _onBlockedSearch = onBlockedSearch;
        _getShieldState = getShieldState;
        _onYoutubeSoftAck = onYoutubeSoftAck;
    }

    public void Start()
    {
        if (_listener is not null)
            return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Server.NoDelay = true;
            _listener.Start(backlog: 32);
            _serverTask = RunAsync(_cts.Token);
            AuditLog.Write($"Extension HTTP bridge listening on 127.0.0.1:{Port}.");
        }
        catch (Exception ex)
        {
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            AuditLog.Write($"Extension HTTP bridge failed to start on 127.0.0.1:{Port}: {ex.Message}");
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { } listener)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
                var acceptedClient = client;
                client = null;
                _ = Task.Run(() => HandleClientAsync(acceptedClient, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                AuditLog.Write($"Extension HTTP bridge accept failed: {ex.Message}");
                await DelayQuietly(TimeSpan.FromMilliseconds(250), ct);
            }
            finally
            {
                client?.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var ownedClient = client;
        try
        {
            using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, ct);
            if (request is null)
                return;

            var (code, body) = Handle(request);
            await WriteResponseAsync(stream, code, body, ct);
        }
        catch (OperationCanceledException)
        {
            // Shutdown path.
        }
        catch (Exception ex)
        {
            try
            {
                using var stream = client.GetStream();
                await WriteResponseAsync(stream, 500, "{\"ok\":false}", CancellationToken.None);
            }
            catch
            {
                // The client may already be gone.
            }

            AuditLog.Write($"Extension HTTP bridge request failed: {ex.Message}");
        }
    }

    private (int Code, string Body) Handle(HttpRequest request)
    {
        if (string.Equals(request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            return (204, "");

        if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.Path, ShieldStatePath, StringComparison.OrdinalIgnoreCase))
        {
            return HandleShieldState();
        }

        if (string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.Path, HeartbeatPath, StringComparison.OrdinalIgnoreCase))
        {
            return HandleHeartbeat(request.Body);
        }

        if (string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.Path, BlockedSearchPath, StringComparison.OrdinalIgnoreCase))
        {
            return HandleBlockedSearch(request.Body);
        }

        if (string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.Path, YoutubeSoftAckPath, StringComparison.OrdinalIgnoreCase))
        {
            _onYoutubeSoftAck?.Invoke();
            return (200, "{\"ok\":true}");
        }

        return (404, "{\"ok\":false}");
    }

    private (int Code, string Body) HandleBlockedSearch(string body)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<ExtensionInfractionEvent>(body);
            if (evt is null
                || !string.Equals(evt.Type, ExtensionInfractionInbox.BlockedSearchType, StringComparison.Ordinal))
            {
                return (400, "{\"ok\":false}");
            }

            _onBlockedSearch(evt.Query ?? "", evt.Match ?? "");
            return (200, "{\"ok\":true}");
        }
        catch
        {
            return (400, "{\"ok\":false}");
        }
    }

    private static (int Code, string Body) HandleHeartbeat(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            if (!string.Equals(type, "heartbeat", StringComparison.OrdinalIgnoreCase))
                return (400, "{\"ok\":false}");

            var browser = root.TryGetProperty("browser", out var b) ? b.GetString() : null;
            var extensionId = root.TryGetProperty("extensionId", out var id) ? id.GetString() : null;
            var version = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            var shieldActive = ExtensionHeartbeatHub.ReadBoolProperty(root, "shieldActive");
            var modelReady = ExtensionHeartbeatHub.ReadBoolProperty(root, "modelReady");

            ExtensionHeartbeatHub.RecordFromPayload(browser, extensionId, version, shieldActive, modelReady);
            return (200, "{\"ok\":true}");
        }
        catch
        {
            return (400, "{\"ok\":false}");
        }
    }

    private (int Code, string Body) HandleShieldState()
    {
        if (_getShieldState is null)
            return (200, "{\"agentRunning\":false,\"active\":false,\"managed\":{\"shieldActive\":false}}");

        try
        {
            var state = _getShieldState();
            var json = JsonSerializer.Serialize(new
            {
                agentRunning = state.AgentRunning,
                active = state.Active,
                managed = state.Managed,
            });
            return (200, json);
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Extension shield-state failed: {ex}");
            return (200, "{\"agentRunning\":true,\"active\":false,\"managed\":{\"shieldActive\":false}}");
        }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var total = 0;
        var headerEnd = -1;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
                return null;

            total += read;
            headerEnd = FindHeaderEnd(buffer, total);
            if (headerEnd >= 0)
                break;
        }

        if (headerEnd < 0)
            return null;

        var headerText = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
            return null;

        var first = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (first.Length < 2)
            return null;

        var contentLength = 0;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':');
            if (colon < 0)
                continue;

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var parsed))
            {
                contentLength = Math.Clamp(parsed, 0, 1024 * 1024);
            }
        }

        var bodyStart = headerEnd + 4;
        var bodyBytesRead = Math.Max(0, total - bodyStart);
        while (bodyBytesRead < contentLength && total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0)
                break;
            total += read;
            bodyBytesRead += read;
        }

        var body = contentLength > 0 && bodyStart < total
            ? Utf8NoBom.GetString(buffer, bodyStart, Math.Min(contentLength, total - bodyStart))
            : "";

        var path = first[1].Split('?', 2)[0];
        return new HttpRequest(first[0], path, body);
    }

    private static int FindHeaderEnd(byte[] buffer, int length)
    {
        for (var i = 3; i < length; i++)
        {
            if (buffer[i - 3] == '\r'
                && buffer[i - 2] == '\n'
                && buffer[i - 1] == '\r'
                && buffer[i] == '\n')
            {
                return i - 3;
            }
        }

        return -1;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int code, string json, CancellationToken ct)
    {
        var body = Utf8NoBom.GetBytes(json);
        var statusText = code switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            _ => "OK",
        };

        var headers =
            $"HTTP/1.1 {code} {statusText}\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
            "Access-Control-Allow-Headers: Content-Type\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, ct);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct);
    }

    private static async Task DelayQuietly(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;
        try
        {
            _serverTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown path.
        }
        _cts?.Dispose();
    }

    private sealed record HttpRequest(string Method, string Path, string Body);
}
