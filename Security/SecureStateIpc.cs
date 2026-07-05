using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace EduGuardAgent.Security;

internal sealed class SecureStateRequest
{
    public string Op { get; set; } = "";        // "write" | "delete"
    public string Path { get; set; } = "";       // absolute path inside the secure folder
    public string? Payload { get; set; }         // final on-disk text (for "write")
}

internal sealed class SecureStateResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Agent-side client: asks the SYSTEM guardian to persist/delete a secure-state file when
/// lockdown has made the folder unwritable to the (merely elevated) agent.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SecureStateIpcClient
{
    // Connecting to a live local pipe is near-instant; this only matters when the guardian
    // is down (lockdown left on with no guardian running), so keep it short — every secure
    // write/delete pays this cost on the UI thread before falling back to a direct write.
    private const int TimeoutMs = 350;

    public static bool TryWrite(string path, string payload, out string? error) =>
        Send(new SecureStateRequest { Op = "write", Path = path, Payload = payload }, out error);

    public static bool TryDelete(string path, out string? error) =>
        Send(new SecureStateRequest { Op = "delete", Path = path }, out error);

    /// <summary>
    /// True when the SYSTEM guardian is actually serving the pipe (a full request/response
    /// round-trip, not just a socket connect). Used to decide whether it is safe to lock the
    /// secure folder SYSTEM-only — locking down with no working guardian would brick every
    /// secure write.
    /// </summary>
    public static bool IsGuardianResponding() =>
        Send(new SecureStateRequest { Op = "ping" }, out _);

    private static bool Send(SecureStateRequest request, out string? error)
    {
        error = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", SecureLockdown.PipeName, PipeDirection.InOut);
            pipe.Connect(TimeoutMs);
            if (pipe.CanRead)
                pipe.ReadMode = PipeTransmissionMode.Message;

            var payload = JsonSerializer.SerializeToUtf8Bytes(request);
            pipe.Write(payload, 0, payload.Length);
            pipe.Flush();

            var response = ReadMessage(pipe);
            var parsed = JsonSerializer.Deserialize<SecureStateResponse>(response);
            if (parsed is { Ok: true })
                return true;

            error = parsed?.Error ?? "guardian rejected the request";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static byte[] ReadMessage(PipeStream pipe)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        do
        {
            var read = pipe.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                break;
            ms.Write(buffer, 0, read);
        }
        while (!pipe.IsMessageComplete);

        return ms.ToArray();
    }
}

/// <summary>
/// Guardian-side server (runs as SYSTEM). Performs secure-state writes/deletes that the
/// agent can no longer do directly under lockdown. Accepts only connections from a process
/// whose image is this same Guardi executable, and only paths inside the secure folder.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SecureStateIpcServer
{
    private static bool _loopErrorLogged;

    public static void RunLoop(CancellationToken token)
    {
        AuditLog.Write($"IPC server listening on pipe {SecureLockdown.PipeName}.");
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = CreateServer();
                server.WaitForConnection();
                Handle(server);
                _loopErrorLogged = false;
            }
            catch (Exception ex)
            {
                // Transient — recreate the pipe and keep serving. Log once per error episode.
                if (!_loopErrorLogged)
                {
                    AuditLog.Write($"IPC server error: {ex.Message}");
                    _loopErrorLogged = true;
                }
                Thread.Sleep(500);
            }
        }
    }

    private static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var authUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        // The agent connects as the interactive (authenticated) user; per-process identity is
        // verified separately by image path once connected.
        security.AddAccessRule(new PipeAccessRule(authUsers, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            SecureLockdown.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Message,
            PipeOptions.None,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private static void Handle(NamedPipeServerStream server)
    {
        SecureStateResponse response;
        try
        {
            if (!IsTrustedClient(server))
            {
                response = new SecureStateResponse { Ok = false, Error = "untrusted client" };
            }
            else
            {
                var request = JsonSerializer.Deserialize<SecureStateRequest>(ReadMessage(server));
                response = request is null
                    ? new SecureStateResponse { Ok = false, Error = "bad request" }
                    : Execute(request);
                AuditLog.Write($"IPC {request?.Op ?? "?"} {(response.Ok ? "ok" : "FAILED: " + response.Error)} ({Path.GetFileName(request?.Path ?? "")})");
            }
        }
        catch (Exception ex)
        {
            response = new SecureStateResponse { Ok = false, Error = ex.Message };
            AuditLog.Write($"IPC handler exception: {ex.Message}");
        }

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
            server.Write(bytes, 0, bytes.Length);
            server.Flush();
        }
        catch
        {
            // Client gone.
        }
    }

    private static SecureStateResponse Execute(SecureStateRequest request)
    {
        // Health check: confirms the guardian is genuinely serving, without touching disk.
        if (request.Op == "ping")
            return new SecureStateResponse { Ok = true };

        // Confine every operation to the secure folder; reject traversal / stray paths.
        var secureRoot = EnsureTrailingSeparator(Path.GetFullPath(SecureDataPaths.Dir));
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(request.Path);
        }
        catch
        {
            return new SecureStateResponse { Ok = false, Error = "invalid path" };
        }

        if (!fullPath.StartsWith(secureRoot, StringComparison.OrdinalIgnoreCase))
            return new SecureStateResponse { Ok = false, Error = "path outside secure root" };

        try
        {
            switch (request.Op)
            {
                case "write":
                    Directory.CreateDirectory(secureRoot);
                    var tmp = fullPath + ".tmp";
                    File.WriteAllText(tmp, request.Payload ?? string.Empty);
                    File.Move(tmp, fullPath, overwrite: true);
                    return new SecureStateResponse { Ok = true };

                case "delete":
                    if (File.Exists(fullPath))
                        File.Delete(fullPath);
                    return new SecureStateResponse { Ok = true };

                default:
                    return new SecureStateResponse { Ok = false, Error = "unknown op" };
            }
        }
        catch (Exception ex)
        {
            return new SecureStateResponse { Ok = false, Error = ex.Message };
        }
    }

    private static bool IsTrustedClient(NamedPipeServerStream server)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(server.SafePipeHandle.DangerousGetHandle(), out var pid))
                return false;

            using var client = Process.GetProcessById((int)pid);
            var clientImage = client.MainModule?.FileName;
            var ours = Environment.ProcessPath;

            if (clientImage is null || ours is null
                || !string.Equals(Path.GetFullPath(clientImage), Path.GetFullPath(ours), StringComparison.OrdinalIgnoreCase))
                return false;

            // Path match alone is forgeable by an admin who drops a malicious build at our exe
            // path. When this build is Authenticode-signed, additionally require the client to
            // carry a valid signature by the same signer. No-op on unsigned builds.
            return AuthenticodeVerifier.MatchesSelfSigner(clientImage);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ReadMessage(PipeStream pipe)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        do
        {
            var read = pipe.Read(buffer, 0, buffer.Length);
            if (read <= 0)
                break;
            ms.Write(buffer, 0, read);
        }
        while (!pipe.IsMessageComplete);

        return ms.ToArray();
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);
}
