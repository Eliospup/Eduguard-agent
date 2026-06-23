using EduGuardAgent.Security;
using EduGuardAgent.Services;

namespace EduGuardAgent.Agent;

internal sealed class AgentHost : IDisposable
{
    private readonly TokenStorage _tokenStorage = new();
    private readonly ApiClient _api;
    private readonly AgentLoop _loop;
    private readonly ScreenshotService _screenshots;
    private readonly AgentModeService _agentMode;
    private CancellationTokenSource? _cts;
    private int? _serverProtocolVersion;

    public AgentHost(
        IAgentNotifier notifier,
        SessionState sessionState,
        UrlBlockingService urlBlocking,
        ExitPinService exitPin,
        GamingTimeTracker gaming,
        YoutubeTimeTracker youtube,
        AgentModeService agentMode,
        PunishmentService punishment,
        LocalModeService localMode)
    {
        SessionState = sessionState;
        UrlBlocking = urlBlocking;
        _agentMode = agentMode;
        _api = new ApiClient(_tokenStorage, notifier);
        _screenshots = new ScreenshotService(_api, notifier);
        _loop = new AgentLoop(_api, notifier, sessionState, urlBlocking, _screenshots, exitPin, gaming, youtube, agentMode, punishment, SupportsPunishmentTelemetry, () => localMode.IsEnabled);
    }

    public void UpdateServerProtocol(int? protocolVersion) => _serverProtocolVersion = protocolVersion;

    private bool SupportsPunishmentTelemetry() =>
        _serverProtocolVersion is >= Config.MinServerProtocolForPunishmentTelemetry;

    public SessionState SessionState { get; }
    public UrlBlockingService UrlBlocking { get; }

    public bool IsEnrolled => _api.HasToken;

    public async Task DiscoverCapabilitiesAsync(IAgentNotifier notifier, CancellationToken ct)
    {
        var result = await _api.FetchCapabilitiesAsync(ct);
        if (!result.Fetched)
            return;

        UpdateServerProtocol(result.ServerProtocolVersion);
        notifier.Log($"Protocol v{result.ServerProtocolVersion} (agent v{Config.ProtocolVersion})");
        notifier.Log($"Server commands: {string.Join(", ", result.ServerCommandTypes)}");

        if (result.ServerProtocolVersion != Config.ProtocolVersion)
        {
            notifier.Log(
                $"Protocol version mismatch — server v{result.ServerProtocolVersion}, agent v{Config.ProtocolVersion}.");
        }

        if (result.UnimplementedByAgent.Count > 0)
        {
            notifier.Log(
                $"Server commands not implemented locally (will report unsupported_command): " +
                $"{string.Join(", ", result.UnimplementedByAgent)}");
        }

        if (result.UnknownToServer.Count > 0)
        {
            notifier.Log(
                $"Agent supports commands omitted by server: {string.Join(", ", result.UnknownToServer)}");
        }
    }

    public void TryLoadSavedEnrollment(IAgentNotifier notifier)
    {
        if (_tokenStorage.TryLoad(out var token) && !string.IsNullOrWhiteSpace(token))
        {
            _api.SetToken(token);
            notifier.EnrollmentChanged(true, $"Saved enrollment loaded ({_agentMode.DisplayName}).");
        }
    }

    public async Task<bool> RegisterAsync(IAgentNotifier notifier, string code, string name, CancellationToken ct)
    {
        var ok = await _api.RegisterAsync(code, name, ct);
        if (!ok)
            return false;

        await DiscoverCapabilitiesAsync(notifier, ct);
        StartServices();
        return true;
    }

    public void StartServices()
    {
        if (_serverProtocolVersion is null)
        {
            // Capabilities fetch should have run before the loop; stay conservative if it did not.
            _serverProtocolVersion = Config.ProtocolVersion - 1;
        }

        UrlBlocking.ReconcileFromDisk();
        _screenshots.Start();
        StartLoop();
    }

    public bool IsLoopRunning => _cts is not null;

    private Task? _loopTask;

    public void StartLoop()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => _loop.RunAsync(_cts.Token));
    }

    public void StopLoop() => StopLoopInternal(wait: false);

    public void StopLoopAndWait(TimeSpan timeout) => StopLoopInternal(wait: true, timeout);

    public Task<bool> ResyncFromServerAsync(CancellationToken ct) =>
        _loop.PullAndApplyServerSettingsAsync(ct);

    public Task<bool> TryRestoreExitPinFromServerAsync(CancellationToken ct) =>
        _loop.TryRestoreExitPinFromServerAsync(ct);

    private void StopLoopInternal(bool wait, TimeSpan timeout = default)
    {
        if (_cts is null)
            return;

        _cts.Cancel();
        _screenshots.Stop();

        if (wait && _loopTask is not null)
        {
            try
            {
                _loopTask.Wait(timeout == default ? TimeSpan.FromSeconds(2) : timeout);
            }
            catch (AggregateException)
            {
                // Loop cancelled during shutdown.
            }
        }

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public void ResetEnrollment(IAgentNotifier notifier)
    {
        StopLoop();
        UrlBlocking.Clear();
        _api.ClearToken();
        notifier.EnrollmentChanged(false, "Enrollment cleared. Enter a new code to reconnect.");
        notifier.SessionRevoked();
    }

    public void Dispose()
    {
        StopLoop();
        _api.Dispose();
        _screenshots.Dispose();
        UrlBlocking.Dispose();
    }
}
