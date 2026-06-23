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
}
