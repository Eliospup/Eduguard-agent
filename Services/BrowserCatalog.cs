using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace EduGuardAgent.Services;

internal enum BrowserKind
{
    Chrome,
    Edge,
    Brave,
    Firefox,
}

internal enum BrowserEngine
{
    Chromium,
    Gecko,
}

/// <summary>
/// A browser Guardi can protect with a force-installed extension. Carries the
/// data needed to detect installation, running processes and per-user profile
/// roots where an installed extension can be found.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ProtectedBrowser
{
    public required BrowserKind Kind { get; init; }
    public required BrowserEngine Engine { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Executable base name without ".exe" (for process lookup).</summary>
    public required string ProcessName { get; init; }

    /// <summary>App Paths registry value name, e.g. "chrome.exe".</summary>
    public required string AppPathsExe { get; init; }

    /// <summary>Common absolute install paths checked as a fallback.</summary>
    public required string[] InstallPaths { get; init; }

    /// <summary>
    /// Per-user-profile relative path (under a user's home dir) to the browser's
    /// data root that contains profile folders / extension stores.
    /// </summary>
    public required string UserDataRelative { get; init; }

    /// <summary>Optional CLI args when Guardi relaunches this browser after install.</summary>
    public string? RestartArguments { get; init; }

    public bool IsInstalled()
    {
        if (Kind == BrowserKind.Firefox)
        {
            return FirefoxEditionHelper.ResolveRunMode() == FirefoxRunMode.Release
                ? FirefoxEditionHelper.IsReleaseInstalled()
                : FirefoxEditionHelper.IsDeveloperEditionInstalled();
        }

        if (TryGetAppPath(AppPathsExe) is { } _)
            return true;

        foreach (var path in InstallPaths)
        {
            if (File.Exists(ExpandEnv(path)))
                return true;
        }

        return false;
    }

    public Process[] GetProcesses()
    {
        try
        {
            return Process.GetProcessesByName(ProcessName);
        }
        catch
        {
            return [];
        }
    }

    public bool IsRunning()
    {
        if (Kind == BrowserKind.Firefox)
        {
            return FirefoxEditionHelper.ResolveRunMode() == FirefoxRunMode.Release
                ? FirefoxEditionHelper.IsReleaseRunning()
                : FirefoxEditionHelper.IsDeveloperEditionRunning();
        }

        var procs = GetProcesses();
        var running = procs.Length > 0;
        foreach (var p in procs)
            p.Dispose();
        return running;
    }

    /// <summary>
    /// True when the browser has at least one visible top-level window (not a
    /// silent background updater). Avoids trapping users when Chrome/Edge keep
    /// helper processes alive after the window is closed.
    /// </summary>
    public bool HasInteractiveWindow()
    {
        if (Kind == BrowserKind.Firefox)
        {
            return FirefoxEditionHelper.ResolveRunMode() == FirefoxRunMode.Release
                ? FirefoxEditionHelper.HasInteractiveReleaseWindow()
                : FirefoxEditionHelper.HasInteractiveDeveloperEditionWindow();
        }

        foreach (var process in GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                    return true;
            }
            catch
            {
                // Access denied on some child processes.
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    public string? TryGetExePath()
    {
        if (Kind == BrowserKind.Firefox)
        {
            return FirefoxEditionHelper.ResolveRunMode() == FirefoxRunMode.Release
                ? FirefoxEditionHelper.GetReleaseExePath()
                : FirefoxEditionHelper.GetDeveloperEditionExePath();
        }

        if (TryGetAppPath(AppPathsExe) is { } fromRegistry)
            return fromRegistry;

        foreach (var path in InstallPaths)
        {
            var expanded = ExpandEnv(path);
            if (File.Exists(expanded))
                return expanded;
        }

        return null;
    }

    /// <summary>User-facing label that reflects Dev Edition enforcement when active.</summary>
    public string EffectiveDisplayName =>
        Kind == BrowserKind.Firefox
            ? FirefoxEditionHelper.ResolveRunMode() == FirefoxRunMode.Release
                ? "Mozilla Firefox"
                : "Firefox Developer Edition"
            : DisplayName;

    private static string? TryGetAppPath(string exe)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (key?.GetValue(null) is string path && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return path;
        }

        return null;
    }

    private static string ExpandEnv(string path) => Environment.ExpandEnvironmentVariables(path);
}

/// <summary>
/// Central registry of the browsers Guardi supports plus the executables of
/// browsers it cannot protect (and therefore blocks).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BrowserCatalog
{
    public static readonly IReadOnlyList<ProtectedBrowser> Protected =
    [
        new ProtectedBrowser
        {
            Kind = BrowserKind.Chrome,
            Engine = BrowserEngine.Chromium,
            DisplayName = "Google Chrome",
            ProcessName = "chrome",
            AppPathsExe = "chrome.exe",
            InstallPaths =
            [
                @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
                @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
                @"%LocalAppData%\Google\Chrome\Application\chrome.exe",
            ],
            UserDataRelative = @"AppData\Local\Google\Chrome\User Data",
        },
        new ProtectedBrowser
        {
            Kind = BrowserKind.Edge,
            Engine = BrowserEngine.Chromium,
            DisplayName = "Microsoft Edge",
            ProcessName = "msedge",
            AppPathsExe = "msedge.exe",
            InstallPaths =
            [
                @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
                @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
            ],
            UserDataRelative = @"AppData\Local\Microsoft\Edge\User Data",
        },
        new ProtectedBrowser
        {
            Kind = BrowserKind.Brave,
            Engine = BrowserEngine.Chromium,
            DisplayName = "Brave",
            ProcessName = "brave",
            AppPathsExe = "brave.exe",
            InstallPaths =
            [
                @"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe",
                @"%ProgramFiles(x86)%\BraveSoftware\Brave-Browser\Application\brave.exe",
                @"%LocalAppData%\BraveSoftware\Brave-Browser\Application\brave.exe",
            ],
            UserDataRelative = @"AppData\Local\BraveSoftware\Brave-Browser\User Data",
        },
        new ProtectedBrowser
        {
            Kind = BrowserKind.Firefox,
            Engine = BrowserEngine.Gecko,
            DisplayName = "Mozilla Firefox",
            ProcessName = "firefox",
            AppPathsExe = "firefox.exe",
            InstallPaths =
            [
                @"%ProgramFiles%\Mozilla Firefox\firefox.exe",
                @"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe",
            ],
            UserDataRelative = @"AppData\Roaming\Mozilla\Firefox\Profiles",
            RestartArguments = "-new-tab about:blank",
        },
    ];

    /// <summary>
    /// Browsers Guardi cannot reliably protect with managed-policy force-install.
    /// Process base names (no ".exe"). Killed when BlockUnsupportedBrowsers is on.
    /// Note: Tor Browser ships as firefox.exe and is intentionally excluded here
    /// to avoid clashing with real Firefox detection.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Unsupported =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["opera"] = "Opera",
            ["opera_gx"] = "Opera GX",
            ["vivaldi"] = "Vivaldi",
            ["yandex"] = "Yandex",
            ["browser"] = "Yandex",
            ["chromium"] = "Chromium",
            ["iron"] = "SRWare Iron",
            ["maxthon"] = "Maxthon",
            ["slimjet"] = "Slimjet",
            ["ucbrowser"] = "UC Browser",
            ["waterfox"] = "Waterfox",
            ["palemoon"] = "Pale Moon",
            ["librewolf"] = "LibreWolf",
            ["cent"] = "Cent Browser",
            ["coccoc"] = "Cốc Cốc",
            ["epicbrowser"] = "Epic Privacy Browser",
        };
}
