using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

namespace EduGuardAgent.Services;

/// <summary>
/// Packs <c>extension/dist/chromium</c> into a dev-signed CRX served locally
/// (same workflow as Firefox local unsigned).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ChromiumLocalPackager
{
    public const string CrxFileName = "guardi-image-shield.crx";
    public const string UpdatesFileName = "updates.xml";
    public const string AgentConfigFileName = "agent-config.json";

    public static string? FindChromiumDistDir()
    {
        var root = ExtensionBundleLocator.FindExtensionSourceRoot();
        if (root is null)
            return null;

        var dist = Path.Combine(root, "dist", "chromium");
        return File.Exists(Path.Combine(dist, "manifest.json")) ? dist : null;
    }

    public static string? FindHostDir()
    {
        var root = ExtensionBundleLocator.FindExtensionSourceRoot();
        if (root is null)
            return null;

        var host = Path.Combine(root, "host");
        return Directory.Exists(host) ? host : null;
    }

    public static bool HasSource() => FindChromiumDistDir() is not null;

    public static bool HasHostArtifacts()
    {
        var host = FindHostDir();
        if (host is null)
            return false;

        return File.Exists(Path.Combine(host, CrxFileName))
            && File.Exists(Path.Combine(host, UpdatesFileName))
            && File.Exists(Path.Combine(host, AgentConfigFileName));
    }

    public static ChromiumHostConfig? TryLoadHostConfig()
    {
        var host = FindHostDir();
        if (host is null)
            return null;

        var configPath = Path.Combine(host, AgentConfigFileName);
        if (!File.Exists(configPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = doc.RootElement;
            var id = root.GetProperty("chromiumExtensionId").GetString() ?? "";
            var updateUrl = root.TryGetProperty("chromeUpdateUrl", out var cu)
                ? cu.GetString() ?? ""
                : "";
            var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(updateUrl))
                return null;

            return new ChromiumHostConfig(id, updateUrl, version, host);
        }
        catch
        {
            return null;
        }
    }

    public static (bool Ok, List<string> Errors) EnsureHostArtifacts()
    {
        var errors = new List<string>();
        var extRoot = ExtensionBundleLocator.FindExtensionSourceRoot();
        if (extRoot is null)
        {
            errors.Add("Extension source folder not found next to the agent.");
            return (false, errors);
        }

        var dist = FindChromiumDistDir();
        if (dist is null)
        {
            errors.Add("Chromium extension not built. Run: cd extension && npm.cmd run build:chromium");
            return (false, errors);
        }

        var hostDir = Path.Combine(extRoot, "host");
        Directory.CreateDirectory(hostDir);
        var crxPath = Path.Combine(hostDir, CrxFileName);
        var manifestPath = Path.Combine(dist, "manifest.json");

        var needsPack = !File.Exists(crxPath)
            || !File.Exists(Path.Combine(hostDir, UpdatesFileName))
            || !File.Exists(Path.Combine(hostDir, AgentConfigFileName))
            || File.GetLastWriteTimeUtc(manifestPath) > File.GetLastWriteTimeUtc(crxPath);

        if (!needsPack)
            return (true, errors);

        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"\"{Path.Combine(extRoot, "scripts", "pack-host.mjs")}\"",
            WorkingDirectory = extRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                errors.Add("Could not start npm run pack:host.");
                return (false, errors);
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(TimeSpan.FromMinutes(5));

            if (process.ExitCode != 0)
            {
                errors.Add($"pack:host failed (exit {process.ExitCode}): {stderr.Trim()}");
                if (!string.IsNullOrWhiteSpace(stdout))
                    errors.Add(stdout.Trim());
                return (false, errors);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"pack:host failed: {ex.Message}");
            return (false, errors);
        }

        return HasHostArtifacts() ? (true, errors) : (false, ["pack:host finished but host artifacts are still missing."]);
    }

    public static void EnsureLocalAgentConfig(ChromiumHostConfig host)
    {
        var hostDir = FindHostDir();
        if (hostDir is null)
            return;

        var configPath = Path.Combine(hostDir, AgentConfigFileName);
        var version = ReadDistVersion() ?? host.Version;
        var json = $$"""
            {
              "chromiumExtensionId": "{{host.ExtensionId}}",
              "chromeUpdateUrl": "http://127.0.0.1:8765/updates.xml",
              "firefoxInstallUrl": "http://127.0.0.1:8765/guardi-image-shield.xpi",
              "version": "{{version}}",
              "hostBase": "http://127.0.0.1:8765"
            }
            """;
        File.WriteAllText(configPath, json);
    }

    public static string? ReadDistVersion()
    {
        var dist = FindChromiumDistDir();
        if (dist is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dist, "manifest.json")));
            return doc.RootElement.GetProperty("version").GetString();
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record ChromiumHostConfig(
    string ExtensionId,
    string UpdateUrl,
    string Version,
    string HostDirectory);
