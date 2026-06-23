using System.Runtime.Versioning;
using System.Text;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Plain-text shield diagnostic for support — policies, install method, versions, heartbeats.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ExtensionShieldDiagnosticReport
{
    private static readonly string[] AuditKeywords =
    [
        "shield",
        "extension",
        "forcelist",
        "heartbeat",
        "Chrome Web Store",
        "firefox",
        "chromium",
        "image shield",
        "load-extension",
        "policy",
    ];

    public static string Build(ImageBlurExtensionService? imageShield = null)
    {
        var cfg = ExtensionConfigResolver.Active;
        var liveness = new HttpExtensionLivenessProbe();
        var presence = cfg is null
            ? null
            : new ExtensionPresenceProbe(cfg.ChromiumExtensionId, cfg.FirefoxAddonId);

        var sb = new StringBuilder();
        sb.AppendLine("Guardi Image Shield — diagnostic report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local)");
        sb.AppendLine($"Machine: {Environment.MachineName}");
        sb.AppendLine();

        AppendConfig(sb, cfg, imageShield);

        if (cfg is not null)
        {
            sb.AppendLine("=== Chromium policies ===");
            sb.AppendLine(ChromiumExtensionPolicy.DescribeForcelist(cfg.ChromiumExtensionId));
            sb.AppendLine(ChromiumExtensionPolicy.DescribeInstallSources());
            if (ChromiumExtensionPolicy.HasStaleLocalhostInstallSources())
                sb.AppendLine("Note: stale dev install sources still present (127.0.0.1 or file:).");
            sb.AppendLine();

            if (!ChromiumUnpackedMode.IsActive)
            {
                var store = ChromiumWebStoreProbe.Check(cfg.ChromiumExtensionId);
                sb.AppendLine("=== Chrome Web Store preflight ===");
                sb.AppendLine($"Status: {store.Status}");
                if (!string.IsNullOrWhiteSpace(store.StoreVersion))
                    sb.AppendLine($"Store version: {store.StoreVersion}");
                if (!string.IsNullOrWhiteSpace(store.Detail))
                    sb.AppendLine($"Detail: {store.Detail}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("=== Browsers ===");
        foreach (var browser in BrowserCatalog.Protected)
            AppendBrowser(sb, browser, cfg, presence, liveness);

        sb.AppendLine("=== Recent shield audit lines ===");
        var audit = AuditLog.ReadRecentLines(25, AuditKeywords);
        if (audit.Count == 0)
            sb.AppendLine("(none)");
        else
        {
            foreach (var line in audit)
                sb.AppendLine(line);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendConfig(
        StringBuilder sb,
        ExtensionRuntimeConfig? cfg,
        ImageBlurExtensionService? imageShield)
    {
        sb.AppendLine("=== Configuration ===");
        sb.AppendLine($"Extension guard: {(Config.ExtensionGuardEnabled ? "on" : "off")}");
        sb.AppendLine($"Running as admin: {(HostsFileManager.IsRunningAsAdmin() ? "yes" : "no")}");
        sb.AppendLine($"Image shield policies active: {(imageShield?.IsActive == true ? "yes" : "no")}");

        var chromiumMode = ChromiumUnpackedMode.IsActive
            ? "Unpacked (--load-extension)"
            : Config.ExtensionGuardEnforceChromium
                ? "Chrome Web Store (forcelist)"
                : "off";
        sb.AppendLine($"Chromium install mode: {chromiumMode}");

        if (ChromiumUnpackedMode.IsActive)
        {
            sb.AppendLine($"Unpacked deploy path: {ChromiumUnpackedDeployer.GetLoadExtensionPath() ?? "(not deployed)"}");
            sb.AppendLine($"Unpacked source built: {(ChromiumUnpackedDeployer.HasSource() ? "yes" : "no")}");
        }

        sb.AppendLine($"Firefox local unsigned: {(Config.ExtensionGuardFirefoxLocalMode ? "on" : "off")}");
        sb.AppendLine($"Firefox Developer Edition: {(FirefoxEditionHelper.IsDeveloperEditionInstalled() ? "installed" : "not found")}");
        sb.AppendLine($"Firefox Release: {(FirefoxEditionHelper.IsReleaseInstalled() ? "installed" : "not found")}");
        if (FirefoxEditionHelper.ShouldBlockRelease)
            sb.AppendLine("Firefox Release: blocked while Dev Edition is available.");

        if (cfg is null)
        {
            sb.AppendLine("Extension config: not ready");
            sb.AppendLine($"Config loader: {ExtensionStoreConfigLoader.LastError ?? "unknown"}");
        }
        else
        {
            sb.AppendLine($"Chromium extension ID: {cfg.ChromiumExtensionId}");
            sb.AppendLine($"Chromium ready: {cfg.IsChromiumReady}");
            sb.AppendLine($"Firefox addon ID: {cfg.FirefoxAddonId}");
            sb.AppendLine($"Firefox store ready: {cfg.IsFirefoxStoreReady}");
        }

        if (!string.IsNullOrWhiteSpace(ExtensionStoreConfigLoader.LastError))
            sb.AppendLine($"Store config note: {ExtensionStoreConfigLoader.LastError}");
        if (!string.IsNullOrWhiteSpace(imageShield?.LastError))
            sb.AppendLine($"Last image shield error: {imageShield.LastError}");

        sb.AppendLine();
    }

    private static void AppendBrowser(
        StringBuilder sb,
        ProtectedBrowser browser,
        ExtensionRuntimeConfig? cfg,
        ExtensionPresenceProbe? presence,
        HttpExtensionLivenessProbe liveness)
    {
        var method = cfg is null
            ? ExtensionInstallMethod.NotApplicable
            : ExtensionInstallRouter.Resolve(browser, cfg);

        var present = presence?.Check(browser) ?? ExtensionPresence.Absent;
        var live = liveness.IsLive(browser.Kind);
        var beat = ExtensionHeartbeatHub.Get(browser.Kind);

        sb.AppendLine($"--- {browser.EffectiveDisplayName} ---");
        sb.AppendLine($"Installed: {(browser.IsInstalled() ? "yes" : "no")}");
        sb.AppendLine($"Running: {(browser.IsRunning() ? "yes" : "no")}");
        sb.AppendLine($"Install method: {ExtensionInstallRouter.Describe(method)}");
        sb.AppendLine($"On disk: {(present.Present ? "yes" : "no")}" +
                        (present.Version is not null ? $" (v{present.Version})" : ""));

        sb.AppendLine(live switch
        {
            true => "Background alive: yes (recent heartbeat)",
            false => "Background alive: no (heartbeat expired)",
            null => beat is null
                ? "Background alive: unknown (no heartbeat yet)"
                : $"Background alive: grace (last heartbeat {(int)(DateTimeOffset.UtcNow - beat.At).TotalSeconds}s ago)",
        });

        if (beat is not null)
        {
            sb.AppendLine(
                $"Last heartbeat: {beat.At:u} — shield={(beat.ShieldActive ? "on" : "off")}, " +
                $"model={(beat.ModelReady ? "ready" : "idle")}, ext={beat.ExtensionId}, v{beat.Version ?? "?"}");
        }
    }
}
