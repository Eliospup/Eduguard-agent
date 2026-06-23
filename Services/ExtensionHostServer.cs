using System.Net;
using System.Runtime.Versioning;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Serves <c>extension/host</c> (CRX + updates.xml) on 127.0.0.1:8765 so Chromium
/// can force-install the local dev-signed build without a separate terminal.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ExtensionHostServer : IDisposable
{
    private static readonly object Gate = new();
    private static ExtensionHostServer? _shared;

    private readonly string _hostDir;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    private ExtensionHostServer(string hostDir) => _hostDir = hostDir;

    public static void EnsureRunning(string hostDir)
    {
        lock (Gate)
        {
            if (_shared is { _disposed: false })
            {
                if (string.Equals(_shared._hostDir, hostDir, StringComparison.OrdinalIgnoreCase))
                    return;

                _shared.Dispose();
                _shared = null;
            }

            _shared = new ExtensionHostServer(hostDir);
            _shared.Start();
        }
    }

    public static void StopShared()
    {
        lock (Gate)
        {
            _shared?.Dispose();
            _shared = null;
        }
    }

    private void Start()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:8765/");
            _listener.Start();
            _loop = Task.Run(() => ServeLoopAsync(_cts.Token));
            AuditLog.Write("Chromium extension host listening on http://127.0.0.1:8765/");
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Chromium extension host FAILED to start on 127.0.0.1:8765 — {ex.Message}");
            throw;
        }
    }

    private async Task ServeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(ct);
                HandleRequest(ctx);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Listener shutting down.
            }
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            var fileName = path == "/" ? ChromiumLocalPackager.UpdatesFileName : path.TrimStart('/');
            var fullPath = Path.GetFullPath(Path.Combine(_hostDir, fileName));

            if (!fullPath.StartsWith(_hostDir, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath))
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
                ctx.Response.Close();
                return;
            }

            var bytes = File.ReadAllBytes(fullPath);
            ctx.Response.ContentType = MimeFor(fileName);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
        catch
        {
            try
            {
                ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                ctx.Response.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string MimeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".crx" => "application/x-chrome-extension",
        ".xpi" => "application/x-xpinstall",
        ".xml" => "text/xml; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        _ => "application/octet-stream",
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // ignore
        }

        _cts?.Dispose();
    }
}
