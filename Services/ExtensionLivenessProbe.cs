using System.Runtime.Versioning;

namespace EduGuardAgent.Services;

/// <summary>
/// Uses <see cref="ExtensionHeartbeatHub"/> heartbeats from the extension background
/// worker to tell whether the shield is actually running, not only present on disk.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class HttpExtensionLivenessProbe : IExtensionLivenessProbe
{
    private static readonly TimeSpan BootstrapGrace = TimeSpan.FromSeconds(35);

    public bool? IsLive(BrowserKind kind)
    {
        var browser = BrowserCatalog.Protected.FirstOrDefault(b => b.Kind == kind);
        if (browser is null || !browser.IsRunning())
            return null;

        var beat = ExtensionHeartbeatHub.Get(kind);
        if (beat is null)
            return null;

        var age = DateTimeOffset.UtcNow - beat.At;
        var ttl = TimeSpan.FromSeconds(Math.Max(20, Config.ExtensionHeartbeatTtlSeconds));

        if (age <= ttl)
            return true;

        if (age <= ttl + BootstrapGrace)
            return null;

        return false;
    }
}
