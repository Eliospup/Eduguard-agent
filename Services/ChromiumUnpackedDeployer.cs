using System.Runtime.Versioning;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Copies <c>extension/dist/chromium</c> to a stable folder and loads it via
/// <c>--load-extension</c> when Guardi restarts the browser (works on personal Windows).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ChromiumUnpackedDeployer
{
    public static string DeployRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Config.AgentDataDir,
            "chromium-unpacked");

    public static bool HasSource() => ChromiumLocalPackager.FindChromiumDistDir() is not null;

    public static string? GetLoadExtensionPath()
    {
        var root = DeployRoot;
        return File.Exists(Path.Combine(root, "manifest.json")) ? root : null;
    }

    public static (bool Ok, bool FilesChanged, List<string> Errors) EnsureDeployed()
    {
        var errors = new List<string>();
        var src = ChromiumLocalPackager.FindChromiumDistDir();
        if (src is null)
        {
            errors.Add("Chromium extension not built. Run: cd extension && npm.cmd run build:chromium");
            return (false, false, errors);
        }

        var manifestSrc = Path.Combine(src, "manifest.json");
        if (!File.Exists(manifestSrc))
        {
            errors.Add("Chromium manifest.json missing in dist/chromium.");
            return (false, false, errors);
        }

        Directory.CreateDirectory(DeployRoot);
        var changed = SyncDirectory(src, DeployRoot);
        if (changed)
            AuditLog.Write($"Chromium unpacked extension deployed -> {DeployRoot}");

        return (true, changed, errors);
    }

    private static bool SyncDirectory(string sourceDir, string targetDir)
    {
        var changed = false;
        var srcManifestTime = File.GetLastWriteTimeUtc(Path.Combine(sourceDir, "manifest.json"));

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(targetDir, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            if (!File.Exists(dest) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(dest))
            {
                File.Copy(file, dest, overwrite: true);
                changed = true;
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(targetDir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            var relative = Path.GetRelativePath(targetDir, dir);
            var srcSub = Path.Combine(sourceDir, relative);
            if (!Directory.Exists(srcSub))
            {
                Directory.Delete(dir, recursive: true);
                changed = true;
            }
        }

        if (!changed && !File.Exists(Path.Combine(targetDir, "manifest.json")))
            changed = true;

        return changed;
    }
}

internal static class ChromiumUnpackedMode
{
    public static bool IsActive =>
        Config.ExtensionGuardChromiumUnpackedMode && ChromiumUnpackedDeployer.HasSource();
}

internal static class ChromiumLaunchHelper
{
    public static bool ShouldLoadUnpacked(ProtectedBrowser browser) =>
        browser.Engine == BrowserEngine.Chromium && ChromiumUnpackedMode.IsActive;

    public static string? BuildLoadExtensionArgument()
    {
        var path = ChromiumUnpackedDeployer.GetLoadExtensionPath();
        if (path is null)
            return null;

        return $"--load-extension=\"{path}\"";
    }

    public static string MergeRestartArguments(ProtectedBrowser browser, string? existing)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(existing))
            parts.Add(existing.Trim());

        if (ShouldLoadUnpacked(browser))
        {
            var loadArg = BuildLoadExtensionArgument();
            if (loadArg is not null)
                parts.Add(loadArg);
        }

        return string.Join(" ", parts);
    }

    public static string BuildRestartArguments(ProtectedBrowser browser)
    {
        string? sessionArgs = null;
        if (Config.BrowserSoftRestartEnabled)
        {
            sessionArgs = browser.Engine switch
            {
                BrowserEngine.Chromium => "--restore-last-session",
                BrowserEngine.Gecko => "-restore",
                _ => null,
            };
        }
        else if (!string.IsNullOrWhiteSpace(browser.RestartArguments))
        {
            sessionArgs = browser.RestartArguments.Trim();
        }

        return MergeRestartArguments(browser, sessionArgs);
    }
}
