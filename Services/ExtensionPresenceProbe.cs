using System.Runtime.Versioning;

namespace EduGuardAgent.Services;
internal readonly record struct ExtensionPresence(bool Present, string? Version)
{
    public static readonly ExtensionPresence Absent = new(false, null);
}

/// <summary>
/// Detects whether the Guardi extension is physically installed in a browser by
/// inspecting the per-user profile folders. The agent runs elevated, so it scans
/// every user's home directory (not just its own) to find the child's profile.
///
/// This is the "installed on disk" signal. A liveness heartbeat
/// (<see cref="IExtensionLivenessProbe"/>) can later confirm the extension is
/// actually running and enabled.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ExtensionPresenceProbe
{
    private readonly string _chromiumExtensionId;
    private readonly string _firefoxAddonId;

    public ExtensionPresenceProbe(string chromiumExtensionId, string firefoxAddonId)
    {
        _chromiumExtensionId = chromiumExtensionId;
        _firefoxAddonId = firefoxAddonId;
    }

    public ExtensionPresence Check(ProtectedBrowser browser)
    {
        try
        {
            return browser.Engine == BrowserEngine.Chromium
                ? CheckChromium(browser)
                : CheckFirefox(browser);
        }
        catch
        {
            // Reading another user's profile can fail (locked, ACL) — treat as
            // unknown-absent; the liveness probe / grace period guard against
            // false positives.
            return ExtensionPresence.Absent;
        }
    }

    private ExtensionPresence CheckChromium(ProtectedBrowser browser)
    {
        if (!ChromiumExtensionInstallState.IsInstalled(browser, _chromiumExtensionId))
            return ExtensionPresence.Absent;

        var version = ChromiumExtensionInstallState.GetInstalledVersion(browser, _chromiumExtensionId);
        return new ExtensionPresence(true, version);
    }

    private ExtensionPresence CheckFirefox(ProtectedBrowser browser) =>
        FirefoxExtensionInstallState.Probe(browser, _firefoxAddonId);

    // Chromium extension version folders look like "1.2.3_0".
    private static string? ParseVersionFolder(string folder)
    {
        var underscore = folder.IndexOf('_');
        var version = underscore >= 0 ? folder[..underscore] : folder;
        return version.Length > 0 && version.All(c => char.IsDigit(c) || c == '.') ? version : null;
    }

    public static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        var len = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < len; i++)
        {
            var na = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
            var nb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
            if (na != nb)
                return na.CompareTo(nb);
        }

        return 0;
    }
}
