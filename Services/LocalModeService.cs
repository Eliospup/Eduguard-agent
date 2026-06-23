using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// When enabled, heartbeat settings and remote commands are ignored — only locally
/// edited settings (persisted in AppData stores) drive enforcement.
/// </summary>
internal sealed class LocalModeService
{
    private readonly LocalModeStore _store = new();

    public bool IsEnabled { get; private set; }
    public DateTimeOffset? EnabledAt { get; private set; }

    public event Action? Changed;

    public void LoadFromStorage()
    {
        var stored = _store.Load();
        IsEnabled = stored.Enabled;
        EnabledAt = stored.EnabledAt;
    }

    public void Enable()
    {
        if (IsEnabled)
            return;

        IsEnabled = true;
        EnabledAt = DateTimeOffset.UtcNow;
        Persist();
        Changed?.Invoke();
    }

    public void Disable()
    {
        if (!IsEnabled)
            return;

        IsEnabled = false;
        EnabledAt = null;
        Persist();
        Changed?.Invoke();
    }

    private void Persist()
    {
        _store.Save(new LocalModeStore.StoredLocalMode
        {
            Enabled = IsEnabled,
            EnabledAt = EnabledAt,
        });
    }
}
