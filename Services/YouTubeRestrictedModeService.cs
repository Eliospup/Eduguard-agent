using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class YouTubeRestrictedModeService : IDisposable
{
    private readonly YouTubeRestrictedModeBackupStore _backupStore = new();
    private readonly ChromiumYouTubeRestrictRegistry _chromium = new();

    public bool IsActive { get; private set; }
    public string? LastError { get; private set; }
    public bool HasAdminRights => HostsFileManager.IsRunningAsAdmin();

    public bool Apply()
    {
        TryReleaseOrphanedState();

        if (!HasAdminRights)
        {
            LastError = HasOrphanedState()
                ? "Administrator rights are required to release YouTube restricted mode left from a previous session."
                : "Administrator rights are required to lock YouTube restricted mode in browsers.";
            IsActive = false;
            AuditLog.Write(LastError);
            return false;
        }

        var registryResult = _chromium.Apply();
        var errors = registryResult.Errors.ToList();
        var state = new YouTubeRestrictedModeState
        {
            Registry = registryResult.Backups,
        };

        if (state.Registry.Count == 0)
        {
            LastError = errors.FirstOrDefault() ?? "YouTube restricted mode policies could not be applied.";
            IsActive = false;
            AuditLog.Write($"YouTube restricted mode apply failed: {LastError}");
            return false;
        }

        _backupStore.Save(state);
        IsActive = true;
        LastError = errors.Count > 0 ? string.Join("; ", errors) : null;

        AuditLog.Write($"YouTube restricted mode locked — Chromium policies: {state.Registry.Count}.");

        if (LastError is not null)
            AuditLog.Write($"YouTube restricted mode partial warnings: {LastError}");

        return true;
    }

    public void Release()
    {
        var state = _backupStore.Load();
        var errors = new List<string>();

        if (state is not null)
            errors.AddRange(_chromium.Restore(state.Registry));

        errors.AddRange(_chromium.ForceRemove());

        if (errors.Count == 0)
        {
            _backupStore.Clear();
            IsActive = false;
            LastError = null;
            AuditLog.Write("YouTube restricted mode released — browser policies restored.");
            return;
        }

        IsActive = false;
        LastError = string.Join("; ", errors);
        AuditLog.Write($"YouTube restricted mode release incomplete — will retry on next startup: {LastError}");
    }

    public void TryReleaseOrphanedState()
    {
        if (!HasOrphanedState())
            return;

        AuditLog.Write("Releasing orphaned YouTube restricted mode policies from a previous session.");
        Release();
    }

    public bool HasOrphanedState() => _backupStore.Load() is not null;

    public void Dispose() => Release();
}
