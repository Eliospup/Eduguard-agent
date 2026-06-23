using System.Runtime.Versioning;

namespace EduGuardAgent.Services;

[SupportedOSPlatform("windows")]
internal sealed class ExtensionInfractionWatcher : IDisposable
{
    private readonly Action<string, string> _onBlockedSearch;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public ExtensionInfractionWatcher(Action<string, string> onBlockedSearch) =>
        _onBlockedSearch = onBlockedSearch;

    public void Start()
    {
        Directory.CreateDirectory(ExtensionInfractionInbox.DirectoryPath);
        ProcessExisting();

        _watcher = new FileSystemWatcher(ExtensionInfractionInbox.DirectoryPath, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _watcher.Created += (_, e) => ProcessFile(e.FullPath);
        _watcher.Changed += (_, e) => ProcessFile(e.FullPath);
    }

    private void ProcessExisting()
    {
        if (!Directory.Exists(ExtensionInfractionInbox.DirectoryPath))
            return;

        foreach (var path in Directory.EnumerateFiles(ExtensionInfractionInbox.DirectoryPath, "*.json"))
            ProcessFile(path);
    }

    private void ProcessFile(string path)
    {
        try
        {
            Thread.Sleep(30);
            var evt = ExtensionInfractionInbox.TryRead(path);
            if (evt is null)
                return;

            if (string.Equals(evt.Type, ExtensionInfractionInbox.BlockedSearchType, StringComparison.Ordinal))
                _onBlockedSearch(evt.Query ?? "", evt.Match ?? "");

            ExtensionInfractionInbox.Delete(path);
        }
        catch
        {
            // ignore malformed / locked files
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher?.Dispose();
    }
}
