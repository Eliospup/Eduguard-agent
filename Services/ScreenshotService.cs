using EduGuardAgent.Agent;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class ScreenshotService : IDisposable
{
    private readonly ApiClient _api;
    private readonly IAgentNotifier _notifier;
    private readonly object _timerGate = new();
    private Timer? _timer;
    private int _uploadInProgress;
    private string? _focusedWindow;

    public ScreenshotService(ApiClient api, IAgentNotifier notifier)
    {
        _api = api;
        _notifier = notifier;
    }

    public void UpdateFocusedWindow(string? focusedWindow) =>
        _focusedWindow = focusedWindow;

    public void Start()
    {
        lock (_timerGate)
        {
            if (_timer is not null)
                return;

            var interval = TimeSpan.FromMinutes(Config.ScreenshotIntervalMinutes);
            _timer = new Timer(
                _ => _ = CaptureAndUploadAsync(ScreenshotTriggers.Scheduled, CancellationToken.None),
                null,
                interval,
                interval);

            AuditLog.Write($"Screenshot scheduler started (every {Config.ScreenshotIntervalMinutes} minutes).");
            _notifier.Log($"Guardi will send a safety screenshot every {Config.ScreenshotIntervalMinutes} minutes.");
        }
    }

    public void Stop()
    {
        lock (_timerGate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public async Task<(bool Success, object Result)> CaptureAndUploadOnDemandAsync(CancellationToken ct) =>
        await CaptureAndUploadAsync(ScreenshotTriggers.OnCommand, ct);

    public void Dispose() => Stop();

    private async Task<(bool Success, object Result)> CaptureAndUploadAsync(string trigger, CancellationToken ct)
    {
        if (!_api.HasToken)
            return (false, new { error = "not_enrolled" });

        if (Interlocked.CompareExchange(ref _uploadInProgress, 1, 0) != 0)
            return (false, new { error = "upload_in_progress" });

        try
        {
            var capturedAt = DateTime.UtcNow;
            var jpeg = await Task.Run(ScreenCapture.CaptureDesktopJpeg, ct);
            var (result, errorBody) = await _api.UploadScreenshotAsync(
                jpeg,
                capturedAt,
                _focusedWindow,
                trigger,
                ct);

            if (result is null)
            {
                var message = FormatUploadError(errorBody);
                _notifier.Log($"Screenshot upload failed: {message}");
                return (false, new { error = "upload_failed", detail = message });
            }

            AuditLog.Write($"Screenshot uploaded ({trigger}) id={result.UploadId}");
            var label = trigger == ScreenshotTriggers.OnCommand ? "on Dom request" : "scheduled";
            _notifier.Log($"Safety screenshot sent to your Dom ({label}).");
            return (true, new
            {
                uploaded = true,
                upload_id = result.UploadId,
                url = result.Url,
                captured_at = capturedAt,
                trigger,
            });
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Screenshot upload failed ({trigger}): {ex.Message}");
            _notifier.Log($"Screenshot upload failed: {ex.Message}");
            return (false, new { error = ex.Message });
        }
        finally
        {
            Interlocked.Exchange(ref _uploadInProgress, 0);
        }
    }

    private static string FormatUploadError(string? errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
            return "unknown error";

        return errorBody.Length > 160 ? errorBody[..160] + "…" : errorBody;
    }
}
