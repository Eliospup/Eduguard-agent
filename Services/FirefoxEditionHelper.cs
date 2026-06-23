using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace EduGuardAgent.Services;

internal enum FirefoxRunMode
{
    Release,
    DeveloperEdition,
}


/// <summary>
/// Firefox Release/Beta ignore signature overrides. Only Developer Edition / Nightly
/// can load unsigned extensions (via policy or about:config).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FirefoxEditionHelper
{
    public static readonly string[] DeveloperEditionPaths =
    [
        @"C:\Program Files\Firefox Developer Edition\firefox.exe",
        @"C:\Program Files (x86)\Firefox Developer Edition\firefox.exe",
    ];

    public static readonly string[] ReleasePaths =
    [
        @"C:\Program Files\Mozilla Firefox\firefox.exe",
        @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe",
    ];

    public static FirefoxRunMode ResolveRunMode() =>
        Config.ExtensionGuardFirefoxLocalMode ? FirefoxRunMode.DeveloperEdition : FirefoxRunMode.Release;

    public static bool ShouldBlockRelease =>
        Config.ExtensionGuardEnforceFirefox
        && ResolveRunMode() == FirefoxRunMode.DeveloperEdition
        && IsDeveloperEditionInstalled()
        && ExtensionConfigResolver.Active?.IsFirefoxStoreReady != true;

    /// <summary>
    /// AMO-signed XPI on Mozilla Firefox Release â€” never Developer Edition.
    /// </summary>
    public static bool UseSignedReleaseTarget =>
        Config.ExtensionGuardEnforceFirefox
        && ResolveRunMode() == FirefoxRunMode.Release
        && ExtensionConfigResolver.Active?.IsFirefoxStoreReady == true;

    public static bool IsDeveloperTarget => ResolveRunMode() == FirefoxRunMode.DeveloperEdition;
    public static bool IsReleaseTarget => ResolveRunMode() == FirefoxRunMode.Release;

    public static bool IsDeveloperEditionInstalled() => GetDeveloperEditionExePath() is not null;

    public static bool IsReleaseInstalled()
    {
        foreach (var path in ReleasePaths)
        {
            if (File.Exists(path))
                return true;
        }

        using var appPath = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe");
        if (appPath?.GetValue(null) is string exePath
            && File.Exists(exePath)
            && !IsDeveloperEditionPath(exePath))
        {
            return true;
        }

        return false;
    }

    public static string? GetReleaseExePath()
    {
        foreach (var path in ReleasePaths)
        {
            if (File.Exists(path))
                return path;
        }

        using var appPath = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe");
        if (appPath?.GetValue(null) is string exePath
            && File.Exists(exePath)
            && !IsDeveloperEditionPath(exePath))
        {
            return exePath;
        }

        return null;
    }

    public static IEnumerable<string> FindReleaseInstallRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in ReleasePaths)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                roots.Add(dir);
        }

        using var mozillaKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Mozilla\Mozilla Firefox");
        if (mozillaKey is not null)
        {
            foreach (var version in mozillaKey.GetSubKeyNames())
            {
                using var versionKey = mozillaKey.OpenSubKey(version);
                var main = versionKey?.GetValue("Main") as string;
                if (string.IsNullOrWhiteSpace(main) || IsDeveloperEditionPath(main))
                    continue;

                var dir = Path.GetDirectoryName(main);
                if (!string.IsNullOrWhiteSpace(dir))
                    roots.Add(dir);
            }
        }

        return roots.Where(Directory.Exists);
    }

    public static bool IsReleaseRunning()
    {
        foreach (var process in GetFirefoxProcesses())
        {
            try
            {
                if (IsReleaseProcess(process))
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

    public static bool HasInteractiveReleaseWindow()
    {
        foreach (var process in GetFirefoxProcesses())
        {
            try
            {
                if (!IsReleaseProcess(process))
                    continue;

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

    public static string? GetDeveloperEditionExePath()
    {
        foreach (var path in DeveloperEditionPaths)
        {
            if (File.Exists(path))
                return path;
        }

        using var appPath = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe");
        if (appPath?.GetValue(null) is string exePath
            && File.Exists(exePath)
            && IsDeveloperEditionPath(exePath))
        {
            return exePath;
        }

        return null;
    }

    public static bool IsDeveloperEditionPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Contains("Developer Edition", StringComparison.OrdinalIgnoreCase);

    public static bool CanInstallLocalUnsigned =>
        Config.ExtensionGuardFirefoxLocalMode && IsDeveloperEditionInstalled();

    public static IEnumerable<string> FindDeveloperEditionInstallRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exe = GetDeveloperEditionExePath();
        if (!string.IsNullOrWhiteSpace(exe))
        {
            var dir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrWhiteSpace(dir))
                roots.Add(dir);
        }

        return roots.Where(Directory.Exists);
    }

    public static Process[] GetFirefoxProcesses()
    {
        try
        {
            return Process.GetProcessesByName("firefox");
        }
        catch
        {
            return [];
        }
    }

    public static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsReleaseProcess(Process process)
    {
        var path = TryGetExecutablePath(process);
        return path is not null && !IsDeveloperEditionPath(path);
    }

    /// <summary>Kills Firefox Release processes; Dev Edition is left running.</summary>
    public static int CloseReleaseProcesses()
    {
        var closed = 0;

        foreach (var process in GetFirefoxProcesses())
        {
            try
            {
                if (!IsReleaseProcess(process))
                    continue;

                if (process.MainWindowHandle != IntPtr.Zero)
                    closed++;

                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort.
            }
            finally
            {
                process.Dispose();
            }
        }

        return closed;
    }

    public static bool IsDeveloperEditionRunning()
    {
        foreach (var process in GetFirefoxProcesses())
        {
            try
            {
                if (IsDeveloperEditionPath(TryGetExecutablePath(process)))
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

    public static bool HasInteractiveDeveloperEditionWindow()
    {
        foreach (var process in GetFirefoxProcesses())
        {
            try
            {
                if (!IsDeveloperEditionPath(TryGetExecutablePath(process)))
                    continue;

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
}
