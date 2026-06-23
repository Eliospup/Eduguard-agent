using System.Diagnostics;
using System.Runtime.Versioning;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Closes and relaunches browsers so enterprise store policies can pull extensions.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class BrowserInstallOrchestrator
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(2000);

    /// <summary>UI thread: (browser display name, message, seconds remaining). 0 = dismiss.</summary>
    public static Action<string, string, int>? RestartCountdownHandler { get; set; }

    public static bool RestartBrowser(ProtectedBrowser browser, Action<string>? log = null)
    {
        var exe = browser.TryGetExePath();
        if (exe is null)
        {
            log?.Invoke($"Could not find {browser.DisplayName} to restart it.");
            return false;
        }

        log?.Invoke($"{UiCopy.MascotName} is restarting {browser.EffectiveDisplayName} to load the shield…");
        WaitForSoftRestartCountdown(browser);
        CloseBrowser(browser);
        Thread.Sleep(SettleDelay);

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            };

            start.Arguments = ChromiumLaunchHelper.BuildRestartArguments(browser);

            AuditLog.Write(
                $"Extension guard: launching {exe}" +
                (string.IsNullOrWhiteSpace(start.Arguments) ? "" : $" with {start.Arguments}"));

            Process.Start(start);
            log?.Invoke($"Restarted {browser.EffectiveDisplayName}.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Failed to restart {browser.EffectiveDisplayName}: {ex.Message}");
            return false;
        }
        finally
        {
            RestartCountdownHandler?.Invoke(browser.EffectiveDisplayName, "", 0);
        }
    }

    private static void WaitForSoftRestartCountdown(ProtectedBrowser browser)
    {
        if (!Config.BrowserSoftRestartEnabled || RestartCountdownHandler is null)
            return;

        var name = browser.EffectiveDisplayName;
        var seconds = Math.Max(3, Config.BrowserSoftRestartWarningSeconds);

        for (var remaining = seconds; remaining > 0; remaining--)
        {
            RestartCountdownHandler.Invoke(
                name,
                ExtensionGuardCopy.SoftRestartCountdownMessage(name, remaining),
                remaining);
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    public static void CloseBrowser(ProtectedBrowser browser)
    {
        if (browser.Kind == BrowserKind.Firefox
            && FirefoxEditionHelper.ResolveRunMode() == FirefoxRunMode.Release)
        {
            for (var pass = 0; pass < 4; pass++)
            {
                FirefoxEditionHelper.CloseReleaseProcesses();
                if (!FirefoxEditionHelper.IsReleaseRunning())
                    break;

                Thread.Sleep(400);
            }

            WaitForExit(browser, ExitTimeout);
            return;
        }

        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var process in browser.GetProcesses())
            {
                try
                {
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

            if (!browser.IsRunning())
                break;

            Thread.Sleep(400);
        }

        if (browser.Engine == BrowserEngine.Chromium)
            ClearChromiumSingletonArtifacts(browser);

        WaitForExit(browser, ExitTimeout);
    }

    private static void ClearChromiumSingletonArtifacts(ProtectedBrowser browser)
    {
        foreach (var home in EnumerateUserHomes())
        {
            var userData = Path.Combine(home, browser.UserDataRelative);
            if (!Directory.Exists(userData))
                continue;

            foreach (var name in new[] { "SingletonLock", "SingletonSocket", "SingletonCookie" })
                TryDeleteFile(Path.Combine(userData, name));
        }
    }

    private static IEnumerable<string> EnumerateUserHomes()
    {
        var usersRoot = Path.GetDirectoryName(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (string.IsNullOrWhiteSpace(usersRoot) || !Directory.Exists(usersRoot))
            yield break;

        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Public", "Default", "Default User", "All Users", "defaultuser0",
        };

        foreach (var dir in Directory.EnumerateDirectories(usersRoot))
        {
            var name = Path.GetFileName(dir);
            if (!skip.Contains(name))
                yield return dir;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }

    private static bool WaitForExit(ProtectedBrowser browser, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!browser.IsRunning())
                return true;
            Thread.Sleep(250);
        }

        return !browser.IsRunning();
    }
}