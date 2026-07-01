using System.Net.Http;
using EduGuardAgent.Commands;
using EduGuardAgent.Models;
using EduGuardAgent.Security;
using EduGuardAgent.Services;

namespace EduGuardAgent.Agent;

internal sealed class AgentLoop
{
    private readonly ApiClient _api;
    private readonly IAgentNotifier _notifier;
    private readonly CommandExecutor _commands;
    private readonly SessionState _sessionState;
    private readonly UrlBlockingService _urlBlocking;
    private readonly ScreenshotService _screenshots;
    private readonly ExitPinService _exitPin;
    private readonly GamingTimeTracker _gaming;
    private readonly YoutubeTimeTracker _youtube;
    private readonly AgentModeService _agentMode;
    private readonly PunishmentService _punishment;
    private readonly Func<bool> _punishmentTelemetryEnabled;
    private readonly Func<bool> _isLocalMode;
    private int _loopCount;
    private bool _punishmentTelemetryWarned;
    private readonly Dictionary<string, DateTimeOffset> _recentBlockNotices = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan BlockNoticeCooldown = TimeSpan.FromSeconds(30);

    public AgentLoop(
        ApiClient api,
        IAgentNotifier notifier,
        SessionState sessionState,
        UrlBlockingService urlBlocking,
        ScreenshotService screenshots,
        ExitPinService exitPin,
        GamingTimeTracker gaming,
        YoutubeTimeTracker youtube,
        AgentModeService agentMode,
        PunishmentService punishment,
        Func<bool> punishmentTelemetryEnabled,
        Func<bool> isLocalMode)
    {
        _api = api;
        _notifier = notifier;
        _sessionState = sessionState;
        _urlBlocking = urlBlocking;
        _screenshots = screenshots;
        _exitPin = exitPin;
        _gaming = gaming;
        _youtube = youtube;
        _agentMode = agentMode;
        _punishment = punishment;
        _punishmentTelemetryEnabled = punishmentTelemetryEnabled;
        _isLocalMode = isLocalMode;
        _commands = new CommandExecutor(notifier, sessionState, urlBlocking);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _notifier.Log($"Agent started — {_agentMode.DisplayName}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_api.HasToken)
                {
                    _notifier.SessionRevoked();
                    break;
                }

                if (_loopCount % Config.HeartbeatEveryLoops == 0)
                {
                    var status = StatusCollector.Collect(_agentMode.Slug);
                    _exitPin.ApplyAuditTo(status);
                    _gaming.ApplyUsageTo(status);
                    _youtube.ApplyUsageTo(status);
                    if (_punishmentTelemetryEnabled())
                    {
                        _punishment.ApplyTo(status);
                    }
                    else if (!_punishmentTelemetryWarned
                             && _punishment.FloorLevelIndex > _agentMode.BaseStrictnessIndex)
                    {
                        _punishmentTelemetryWarned = true;
                        _notifier.Log(
                            "Punishment is active on this device but the server protocol is below v3 — " +
                            "the dashboard cannot see the real enforced mode until the backend is upgraded.");
                    }
                    ImageShieldPolicy.Current?.ApplyTo(status, _agentMode.Slug);
                    var heartbeat = await _api.SendHeartbeatAsync(status, ct);
                    var ok = heartbeat?.Ok ?? false;
                    if (!_isLocalMode() && heartbeat?.Settings is { } settings)
                        ServerSettingsApplier.Apply(_notifier, settings);

                    _screenshots.UpdateFocusedWindow(status.FocusedWindow);
                    _notifier.HeartbeatUpdated(status, ok);
                    EnforceBlockedApps(status.RunningApps);
                }

                if (!_isLocalMode())
                {
                    var commands = await _api.GetCommandsAsync(ct);
                    if (commands?.Commands is { Count: > 0 })
                    {
                        foreach (var command in commands.Commands)
                        {
                            var (success, result) = string.Equals(command.Type, "screenshot", StringComparison.OrdinalIgnoreCase)
                                ? await _screenshots.CaptureAndUploadOnDemandAsync(ct)
                                : _commands.Execute(command);

                            await _api.ReportCommandResultAsync(command.Id, success, result, ct);
                            _notifier.CommandExecuted(command.Type, success);
                        }
                    }
                }

                _loopCount++;
            }
            catch (HttpRequestException ex)
            {
                _notifier.Log($"Agent loop error: {ex.Message}");
                AuditLog.Write($"Agent loop error: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _notifier.Log($"Agent loop error: {ex.Message}");
                AuditLog.Write($"Agent loop error: {ex}");
            }

            await Task.Delay(Config.LoopIntervalMs, ct);
        }
    }

    /// <summary>One-shot heartbeat used when leaving local mode to pull Dom settings.</summary>
    public async Task<bool> PullAndApplyServerSettingsAsync(CancellationToken ct)
    {
        if (!_api.HasToken)
            return false;

        var status = StatusCollector.Collect(_agentMode.Slug);
        _exitPin.ApplyAuditTo(status);
        _gaming.ApplyUsageTo(status);
        _youtube.ApplyUsageTo(status);
        if (_punishmentTelemetryEnabled())
            _punishment.ApplyTo(status);
        ImageShieldPolicy.Current?.ApplyTo(status, _agentMode.Slug);

        var heartbeat = await _api.SendHeartbeatAsync(status, ct);
        if (heartbeat?.Settings is { } settings)
            ServerSettingsApplier.Apply(_notifier, settings);

        return heartbeat?.Ok ?? false;
    }

    /// <summary>Restores only the exit PIN from the server (safe while local mode is on).</summary>
    public async Task<bool> TryRestoreExitPinFromServerAsync(CancellationToken ct)
    {
        if (!_api.HasToken)
            return false;

        var status = StatusCollector.Collect(_agentMode.Slug);
        _exitPin.ApplyAuditTo(status);
        _gaming.ApplyUsageTo(status);
        _youtube.ApplyUsageTo(status);
        if (_punishmentTelemetryEnabled())
            _punishment.ApplyTo(status);
        ImageShieldPolicy.Current?.ApplyTo(status, _agentMode.Slug);

        var heartbeat = await _api.SendHeartbeatAsync(status, ct);
        if (heartbeat?.Settings is not { } settings || !settings.TryGetExitPin(out var pin))
            return false;

        // In local mode the Dom manages the PIN on-device; a PIN still configured on the web
        // dashboard must not override or resurrect it.
        if (_isLocalMode())
            return _exitPin.IsRequired;

        _exitPin.UpdateFromServer(pin);
        return _exitPin.IsRequired;
    }

    private void EnforceBlockedApps(IReadOnlyList<string> runningApps)
    {
        foreach (var app in runningApps)
        {
            if (!_sessionState.IsBlocked(app))
                continue;

            if (!_sessionState.TryGetBlockCategory(app, out var category))
                category = AppBlockCategory.DomManual;

            var processName = app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? app[..^4]
                : app;

            var killedAny = false;
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    killedAny = true;
                    _notifier.Log($"Blocked app killed: {app}");
                }
                catch
                {
                    // Access denied for some processes.
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (killedAny)
                NotifyAppBlocked(app, category);
        }
    }

    private void NotifyAppBlocked(string processName, AppBlockCategory category)
    {
        var now = DateTimeOffset.UtcNow;
        if (category != AppBlockCategory.VpnShield
            && _recentBlockNotices.TryGetValue(processName, out var lastShown)
            && now - lastShown < BlockNoticeCooldown)
        {
            return;
        }

        _recentBlockNotices[processName] = now;
        _notifier.AppClosedByGuardi(processName, category);
    }
}
