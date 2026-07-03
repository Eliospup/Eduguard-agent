using EduGuardAgent.Models;
using EduGuardAgent.Security;

namespace EduGuardAgent;

internal static partial class Config
{
    public static string BaseUrl => AgentEndpointResolver.Resolve();

    public const string AgentDataDir = "EduGuard";
    public const string TokenFileName = "agent.dat";
    public const string ExitPinFileName = "exit_pin.dat";
    public const string SubProfileFileName = "sub_profile.json";
    public const string AuditLogFileName = "audit.log";

    // Rotate audit.log when it exceeds this size; keep AuditLogArchivedFiles older segments.
    public const long AuditLogMaxBytes = 5 * 1024 * 1024;
    public const int AuditLogArchivedFiles = 2;

    public const int ProtocolVersion = 3;

    /// <summary>Server must advertise this protocol version before the agent sends punishment telemetry in heartbeats.</summary>
    public const int MinServerProtocolForPunishmentTelemetry = 3;

    public const int LoopIntervalMs = 5000;
    public const int HeartbeatEveryLoops = 2;

    public const string DefaultModeSlug = AgentModeSlugs.TrustedSub;

    // Seconds after study time starts (or agent launch mid-window) before study
    // infractions count. Apps are still closed immediately.
    public const int StudyTimeActivationGraceSeconds = 60;

    public const int ScreenshotIntervalMinutes = 5;
    public const int ScreenshotJpegQuality = 72;
    public const int ScreenshotMaxWidthPixels = 1920;

    // --- Guardi Image Shield (NSFW blur) browser extension -----------------
    // Published on Chrome Web Store + Firefox AMO. IDs/URLs live in
    // extension/store-config.json (see docs/EXTENSION_STORE_PUBLISHING.md).
    public const string ImageShieldExtensionId = "pooilkajkfmogajdafmaphmjecofpbbk";
    public const string ImageShieldChromeUpdateUrl =
        "https://clients2.google.com/service/update2/crx";
    public const string ImageShieldFirefoxAddonId = "image-shield@guardi.app";
    public const string ImageShieldFirefoxInstallUrl =
        "https://github.com/Eliospup/Eduguard-agent/releases/download/extension-v0.8.43/guardi-image-shield.xpi";

    // --- Extension enforcement (mandatory install + anti-tamper) -----------
    // Master switch for the guard that detects, force-installs and enforces the
    // Guardi extensions across the supported browsers.
    public const bool ExtensionGuardEnabled = true;

    // Kill browsers that cannot be policy-protected (Opera, Vivaldi, portable
    // Chromium builds, …) so the child cannot dodge the shield with them.
    public const bool BlockUnsupportedBrowsers = true;

    // Lowest acceptable installed extension version (Chromium). Empty disables
    // the update check. Bump this when a critical extension update ships.
    public const string ImageShieldMinVersion = "";

    // Seconds to wait for the browser store to download the extension before
    // closing the browser. First install from the Web Store can take a few minutes.
    public const int ExtensionInstallGraceSeconds = 300;

    // Chrome Web Store — extension published (unlisted). See extension/store-config.json.
    public static bool ExtensionGuardEnforceChromium = true;

    // Dev: Guardi restarts Chrome/Edge/Brave with --load-extension=dist/chromium (works on personal Windows).
    // Prod: false — force-install published build from Chrome Web Store only.
    // Debug-only: set ExtensionGuardChromiumUnpackedMode = true for local Chromium sideload.

    // Firefox Release: local unsigned XPI via policies.json (single-PC mode).
    public static bool ExtensionGuardEnforceFirefox = true;

    // Fullscreen overlay during the grace/download phase. Off by default so you
    // are not blocked while the browser is still pulling the extension.
    public static bool ExtensionGuardShowInstallingOverlay => false;

    // How often the enforcement loop checks browsers/extension presence.
    public const int ExtensionGuardTickMs = 2000;

    // Extension background must ping within this window while the browser runs.
    public const int ExtensionHeartbeatTtlSeconds = 45;

    // Warn before Guardi restarts a browser, then restore the last session on relaunch.
    public static bool BrowserSoftRestartEnabled = true;
    public const int BrowserSoftRestartWarningSeconds = 10;

    // Kiosk guard polls foreground windows and re-hides the taskbar.
    public const int KioskGuardIntervalMs = 300;

    // WPF shell watchdog: re-maximize EduGuard if the kiosk window was resized/minimized.
    public const int KioskShellWatchIntervalMs = 1000;
}
