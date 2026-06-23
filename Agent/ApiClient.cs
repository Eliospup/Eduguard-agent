using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using EduGuardAgent.Models;
using EduGuardAgent.Security;
using EduGuardAgent.Services;

namespace EduGuardAgent.Agent;

internal sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly TokenStorage _tokenStorage;
    private readonly IAgentNotifier _notifier;
    private string? _token;

    public ApiClient(TokenStorage tokenStorage, IAgentNotifier notifier)
    {
        _tokenStorage = tokenStorage;
        _notifier = notifier;
        // Bypass the Windows system proxy — a dead local forwarder (e.g. 127.0.0.1:10129)
        // otherwise blocks every heartbeat with connection refused.
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(Config.BaseUrl),
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    public bool HasToken => !string.IsNullOrWhiteSpace(_token);

    public async Task<CapabilitySyncResult> FetchCapabilitiesAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("/api/public/agent/capabilities", ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var message = $"Capabilities fetch failed ({(int)response.StatusCode}): {body}";
                AuditLog.Write(message);
                _notifier.Log(message);
                return CapabilitySyncResult.FetchFailed();
            }

            var capabilities = await response.Content.ReadFromJsonAsync<CapabilitiesResponse>(cancellationToken: ct);
            if (capabilities is null)
            {
                const string message = "Capabilities fetch failed: empty response.";
                AuditLog.Write(message);
                _notifier.Log(message);
                return CapabilitySyncResult.FetchFailed();
            }

            return AgentCapabilityRegistry.SyncWithServer(capabilities);
        }
        catch (Exception ex)
        {
            var message = $"Capabilities fetch error: {ex.Message}";
            AuditLog.Write(message);
            _notifier.Log(message);
            return CapabilitySyncResult.FetchFailed();
        }
    }

    public void SetToken(string token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearToken()
    {
        _token = null;
        _http.DefaultRequestHeaders.Authorization = null;
        _tokenStorage.Wipe();
    }

    public async Task<bool> RegisterAsync(string code, string name, CancellationToken ct)
    {
        var request = new RegisterRequest
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name,
            OsInfo = new OsInfo
            {
                Version = GetOsVersion(),
                Hostname = Environment.MachineName,
            },
        };

        using var response = await _http.PostAsJsonAsync("/api/public/agent/register", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _notifier.Log($"Registration failed ({(int)response.StatusCode}): {body}");
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken: ct);
        if (result is null || string.IsNullOrWhiteSpace(result.AgentToken))
        {
            _notifier.Log("Registration failed: empty token in response.");
            return false;
        }

        _tokenStorage.Save(result.AgentToken);
        SetToken(result.AgentToken);
        AuditLog.Write($"Registered agent {result.AgentId}.");
        _notifier.EnrollmentChanged(true, $"Enrolled as {result.AgentId}");
        return true;
    }

    public async Task<HeartbeatResponse?> SendHeartbeatAsync(HeartbeatRequest status, CancellationToken ct)
    {
        using var response = await PostAuthorizedAsync("/api/public/agent/heartbeat", status, ct);
        if (response is null)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            await LogFailureAsync("heartbeat", response, ct);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<HeartbeatResponse>(AgentJson.Options, ct)
            ?? new HeartbeatResponse { Ok = true };
    }

    public async Task<CommandsResponse?> GetCommandsAsync(CancellationToken ct)
    {
        using var response = await GetAuthorizedAsync("/api/public/agent/commands", ct);
        if (response is null)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            await LogFailureAsync("commands", response, ct);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<CommandsResponse>(cancellationToken: ct)
            ?? new CommandsResponse();
    }

    public async Task<(UploadResponse? Response, string? ErrorBody)> UploadScreenshotAsync(
        byte[] jpegBytes,
        DateTime capturedAtUtc,
        string? focusedWindow,
        string trigger,
        CancellationToken ct)
    {
        if (jpegBytes.Length == 0)
            return (null, "{\"error\":\"empty_image\"}");

        using var content = ScreenshotMultipartBuilder.Build(
            jpegBytes,
            capturedAtUtc,
            trigger,
            focusedWindow);

        using var response = await PostAuthorizedMultipartAsync("/api/public/agent/upload", content, ct);
        if (response is null)
            return (null, "{\"error\":\"not_authenticated\"}");

        if (response.IsSuccessStatusCode)
        {
            var parsed = await response.Content.ReadFromJsonAsync<UploadResponse>(cancellationToken: ct)
                ?? new UploadResponse { Ok = true };
            return (parsed, null);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        await LogFailureAsync("screenshot upload", response, ct);
        return (null, body);
    }

    public async Task<bool> ReportCommandResultAsync(
        string commandId,
        bool success,
        object result,
        CancellationToken ct)
    {
        var payload = new CommandResultRequest
        {
            Status = success ? "done" : "failed",
            Result = result,
        };

        using var response = await PostAuthorizedAsync(
            $"/api/public/agent/commands/{commandId}/result",
            payload,
            ct);

        if (response is null)
            return false;

        if (response.IsSuccessStatusCode)
            return true;

        await LogFailureAsync("command result", response, ct);
        return false;
    }

    private async Task<HttpResponseMessage?> PostAuthorizedAsync<T>(
        string path,
        T body,
        CancellationToken ct)
    {
        if (!HasToken)
            return null;

        var response = await _http.PostAsJsonAsync(path, body, AgentJson.Options, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            HandleUnauthorized();
            response.Dispose();
            return null;
        }

        return response;
    }

    private async Task<HttpResponseMessage?> GetAuthorizedAsync(string path, CancellationToken ct)
    {
        if (!HasToken)
            return null;

        var response = await _http.GetAsync(path, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            HandleUnauthorized();
            response.Dispose();
            return null;
        }

        return response;
    }

    private async Task<HttpResponseMessage?> PostAuthorizedMultipartAsync(
        string path,
        HttpContent content,
        CancellationToken ct)
    {
        if (!HasToken)
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));

        var response = await _http.PostAsync(path, content, timeoutCts.Token);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            HandleUnauthorized();
            response.Dispose();
            return null;
        }

        return response;
    }

    private void HandleUnauthorized()
    {
        AuditLog.Write("Token rejected (401). Wiping local credentials.");
        ClearToken();
        _notifier.SessionRevoked();
    }

    private async Task LogFailureAsync(string action, HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var message = $"{action} failed ({(int)response.StatusCode}): {body}";
        AuditLog.Write(message);
        _notifier.Log(message);
    }

    private static string GetOsVersion()
    {
        var version = Environment.OSVersion.Version;
        return $"Windows {version.Major}.{version.Minor} (build {version.Build})";
    }

    public void Dispose() => _http.Dispose();
}
