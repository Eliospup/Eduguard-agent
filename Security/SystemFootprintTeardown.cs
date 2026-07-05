using System.Runtime.Versioning;
using EduGuardAgent.Services;

namespace EduGuardAgent.Security;

/// <summary>
/// Reverses every machine-wide change Guardi makes *outside* its own data folders — the
/// modifications that would otherwise survive deleting <c>ProgramData\EduGuard</c>: the hosts
/// blocklist, SafeSearch / YouTube-restricted enforcement, the Cloudflare Family DNS shield,
/// Chromium and Firefox managed policies, the native-messaging host registration, and the
/// EduGuard root certificate in the machine trust store.
///
/// Runs elevated as part of <see cref="SecurityTeardown"/> / <c>--uninstall</c>. Every step is
/// best-effort and isolated, so one failure can never strand the rest. Must run BEFORE the data
/// folders are deleted, because several reverts read their original state from a persisted
/// backup that lives under <c>ProgramData\EduGuard</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SystemFootprintTeardown
{
    public static void RevertAll()
    {
        AuditLog.Write("System footprint teardown started.");

        // Extension policies (Chromium + Firefox) — restores the exact registry / policies.json
        // state captured at install time from the persisted backup store.
        Try(() => new ImageBlurExtensionService().Release(), "browser extension policies");

        // Hosts-based enforcement that keeps its own restore backups.
        Try(() => new SafeSearchService().Release(), "SafeSearch enforcement");
        Try(() => new YouTubeRestrictedModeService().Release(), "YouTube restricted mode");

        // Belt-and-suspenders: force-remove any policy residue a backup wouldn't cover
        // (e.g. a crash between apply and backup, or a manual re-apply).
        Try(() => new ChromiumSafeSearchRegistry().ForceRemove(), "Chromium SafeSearch policies");
        Try(() => new ChromiumYouTubeRestrictRegistry().ForceRemove(), "Chromium YouTube policies");
        Try(() => ChromiumExtensionPolicy.StripForcelist(Config.ImageShieldExtensionId), "Chromium forcelist");
        Try(() => ChromiumExtensionPolicy.StripIncognitoSettings(Config.ImageShieldExtensionId), "Chromium ExtensionSettings");
        Try(() => ChromiumExtensionPolicy.StripManagedStorage(Config.ImageShieldExtensionId), "Chromium 3rdparty managed storage");
        Try(() => new FirefoxExtensionPolicy().Restore(Config.ImageShieldFirefoxAddonId), "Firefox policies");

        // System DNS + hosts file.
        Try(() => new FamilyDnsShieldService().TryReleaseOrphanedState(), "Family DNS shield");
        // Belt-and-suspenders: drop the DoH-resolver firewall rules even if no backup file was
        // left to drive TryReleaseOrphanedState above.
        Try(FamilyDnsShieldService.RemoveDohFirewallBlock, "DoH firewall rules");
        Try(() => new HostsFileManager().Clear(), "hosts blocklist");

        // Registry + on-disk native-messaging registration.
        Try(NativeMessagingHostRegistry.Unregister, "native messaging host");

        // Trusted root certificate.
        Try(LocalCertificateAuthority.RemoveFromTrustStore, "root certificate");

        AuditLog.Write("System footprint teardown complete.");
    }

    private static void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AuditLog.Write($"Teardown step failed ({what}): {ex.Message}");
        }
    }
}
