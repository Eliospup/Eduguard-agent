using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class SafeSearchService : IDisposable
{
    private readonly SafeSearchBackupStore _backupStore = new();
    private readonly ChromiumSafeSearchRegistry _chromium = new();
    private readonly FirefoxSafeSearchPolicy _firefox = new();

    public bool IsActive { get; private set; }
    public string? LastError { get; private set; }
    public bool HasAdminRights => HostsFileManager.IsRunningAsAdmin();

    public bool Apply()
    {
        // Always try to release a previous session first — even if this launch
        // will not re-apply (e.g. missing admin). Policies must not outlive the app.
        TryReleaseOrphanedState();

        if (!HasAdminRights)
        {
            LastError = HasOrphanedState()
                ? "Administrator rights are required to release SafeSearch left from a previous session."
                : "Administrator rights are required to lock SafeSearch in browsers.";
            IsActive = false;
            AuditLog.Write(LastError);
            return false;
        }

        var registryResult = _chromium.Apply();
        var firefoxResult = _firefox.Apply(_backupStore);

        var errors = registryResult.Errors.Concat(firefoxResult.Errors).ToList();
        var state = new SafeSearchState
        {
            Registry = registryResult.Backups,
            FirefoxPolicies = firefoxResult.Backups,
        };

        if (state.Registry.Count == 0 && state.FirefoxPolicies.Count == 0)
        {
            LastError = errors.FirstOrDefault() ?? "SafeSearch policies could not be applied.";
            IsActive = false;
            AuditLog.Write($"SafeSearch apply failed: {LastError}");
            return false;
        }

        _backupStore.Save(state);
        IsActive = true;
        LastError = errors.Count > 0 ? string.Join("; ", errors) : null;

        AuditLog.Write(
            $"SafeSearch locked — Chromium policies: {state.Registry.Count}, " +
            $"Firefox installs: {state.FirefoxPolicies.Count}.");

        if (LastError is not null)
            AuditLog.Write($"SafeSearch partial warnings: {LastError}");

        return true;
    }

    public void Release()
    {
        var state = _backupStore.Load();
        var errors = new List<string>();

        if (state is not null)
        {
            errors.AddRange(_chromium.Restore(state.Registry));
            errors.AddRange(_firefox.Restore(state.FirefoxPolicies));
        }

        // Safety net: strip our known keys even if the backup is missing or restore failed.
        errors.AddRange(_chromium.ForceRemove());
        errors.AddRange(_firefox.ForceRemove());

        if (errors.Count == 0)
        {
            _backupStore.Clear();
            IsActive = false;
            LastError = null;
            AuditLog.Write("SafeSearch released — browser policies restored.");
            return;
        }

        IsActive = false;
        LastError = string.Join("; ", errors);
        AuditLog.Write($"SafeSearch release incomplete — will retry on next startup: {LastError}");
    }

    /// <summary>
    /// Call as early as possible on startup so policies from a crash or kill do not linger.
    /// </summary>
    public void TryReleaseOrphanedState()
    {
        if (!HasOrphanedState())
            return;

        AuditLog.Write("Releasing orphaned SafeSearch policies from a previous session.");
        Release();
    }

    public bool HasOrphanedState() => _backupStore.Load() is not null;

    public void Dispose() => Release();
}
