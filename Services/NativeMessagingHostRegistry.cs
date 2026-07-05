using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace EduGuardAgent.Services;

[SupportedOSPlatform("windows")]
internal static class NativeMessagingHostRegistry
{
    private static readonly string NativeDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir,
        "native");

    public static string ResolveAgentExecutable()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(processPath))
        {
            return processPath;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "EduGuardAgent.exe");
        if (File.Exists(candidate))
            return candidate;

        return processPath ?? candidate;
    }

    public static void Register()
    {
        try
        {
            var exePath = ResolveAgentExecutable();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return;

            Directory.CreateDirectory(NativeDir);
            var launcherPath = Path.Combine(NativeDir, "guardi-native-host.cmd");
            File.WriteAllText(
                launcherPath,
                $"@echo off\r\n\"{exePath}\" --native-messaging\r\n",
                Encoding.UTF8);

            var manifestPath = Path.Combine(NativeDir, $"{GuardiNativeMessaging.HostName}.json");
            var manifest = new
            {
                name = GuardiNativeMessaging.HostName,
                description = "Guardi EduGuard Agent native messaging host",
                path = launcherPath,
                type = "stdio",
                allowed_extensions = new[]
                {
                    Config.ImageShieldFirefoxAddonId,
                    Config.ImageShieldExtensionId,
                },
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            RegisterHive(Registry.CurrentUser, manifestPath);
            RegisterHive(Registry.LocalMachine, manifestPath);
        }
        catch
        {
            // Best-effort — HTTP infraction fallback still works.
        }
    }

    private static void RegisterHive(RegistryKey hive, string manifestPath)
    {
        using var key = hive.CreateSubKey($@"Software\Mozilla\NativeMessagingHosts\{GuardiNativeMessaging.HostName}");
        key.SetValue("", manifestPath, RegistryValueKind.String);

        using var chromeKey = hive.CreateSubKey($@"Software\Google\Chrome\NativeMessagingHosts\{GuardiNativeMessaging.HostName}");
        chromeKey.SetValue("", manifestPath, RegistryValueKind.String);

        using var edgeKey = hive.CreateSubKey($@"Software\Microsoft\Edge\NativeMessagingHosts\{GuardiNativeMessaging.HostName}");
        edgeKey.SetValue("", manifestPath, RegistryValueKind.String);
    }

    /// <summary>
    /// Fully removes the native-messaging registration for a clean uninstall: deletes the
    /// registry keys in both hives (all three browser families) and the on-disk manifest/launcher
    /// directory. Best-effort and safe to call when nothing was registered.
    /// </summary>
    public static void Unregister()
    {
        foreach (var (hive, hiveName) in new[] { (Registry.CurrentUser, "HKCU"), (Registry.LocalMachine, "HKLM") })
        {
            foreach (var vendor in new[] { "Mozilla", @"Google\Chrome", @"Microsoft\Edge" })
            {
                var path = $@"Software\{vendor}\NativeMessagingHosts\{GuardiNativeMessaging.HostName}";
                try
                {
                    hive.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                }
                catch (Exception ex)
                {
                    Security.AuditLog.Write($"Registry delete failed ({hiveName}\\{path}): {ex.Message}");
                    TryRegExeDelete(hiveName, path);
                }
            }
        }

        try
        {
            if (Directory.Exists(NativeDir))
                Directory.Delete(NativeDir, recursive: true);
        }
        catch
        {
            // Best-effort; the folder is removed with the data dir anyway.
        }
    }

    private static void TryRegExeDelete(string hive, string path)
    {
        try
        {
            var fullKey = $"{hive}\\{path}";
            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", $"delete \"{fullKey}\" /f")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi)?.WaitForExit(5000);
        }
        catch
        {
            // Last resort failed.
        }
    }
}
