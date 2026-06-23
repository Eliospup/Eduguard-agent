namespace EduGuardAgent.Profiles;

internal sealed class VpnCatalogEntry
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Apps { get; init; }
    public required IReadOnlyList<string> Hosts { get; init; }
}

internal static class VpnBlocklist
{
    public static IReadOnlyList<VpnCatalogEntry> Entries { get; } =
    [
        new()
        {
            Name = "NordVPN",
            Apps = ["nordvpn.exe"],
            Hosts = ["nordvpn.com", "nordaccount.com"],
        },
        new()
        {
            Name = "ExpressVPN",
            Apps = ["expressvpn.exe", "expressvpndaemon.exe"],
            Hosts = ["expressvpn.com"],
        },
        new()
        {
            Name = "Surfshark",
            Apps = ["surfshark.exe"],
            Hosts = ["surfshark.com"],
        },
        new()
        {
            Name = "Proton VPN",
            Apps = ["protonvpn.exe", "protonvpnservice.exe"],
            Hosts = ["protonvpn.com"],
        },
        new()
        {
            Name = "CyberGhost",
            Apps = ["cyberghost.exe"],
            Hosts = ["cyberghostvpn.com"],
        },
        new()
        {
            Name = "Private Internet Access",
            Apps = ["pia-client.exe", "pia-service.exe"],
            Hosts = ["privateinternetaccess.com"],
        },
        new()
        {
            Name = "Mullvad",
            Apps = ["mullvad-vpn.exe"],
            Hosts = ["mullvad.net"],
        },
        new()
        {
            Name = "Windscribe",
            Apps = ["windscribe.exe"],
            Hosts = ["windscribe.com"],
        },
        new()
        {
            Name = "Hotspot Shield",
            Apps = ["hotspotshield.exe", "hss.exe"],
            Hosts = ["hotspotshield.com"],
        },
        new()
        {
            Name = "TunnelBear",
            Apps = ["tunnelbear.exe"],
            Hosts = ["tunnelbear.com"],
        },
        new()
        {
            Name = "OpenVPN",
            Apps = ["openvpn.exe", "openvpn-gui.exe"],
            Hosts = ["openvpn.net", "openvpn.org"],
        },
        new()
        {
            Name = "WireGuard",
            Apps = ["wireguard.exe"],
            Hosts = ["wireguard.com"],
        },
        new()
        {
            Name = "Cloudflare WARP",
            Apps = ["warp-cli.exe", "cloudflarewarp.exe"],
            Hosts = ["cloudflarewarp.com"],
        },
        new()
        {
            Name = "VyprVPN",
            Apps = ["vyprvpn.exe"],
            Hosts = ["vyprvpn.com", "goldenfrog.com"],
        },
        new()
        {
            Name = "PureVPN",
            Apps = ["purevpn.exe"],
            Hosts = ["purevpn.com"],
        },
        new()
        {
            Name = "IVPN",
            Apps = ["ivpn.exe"],
            Hosts = ["ivpn.net"],
        },
        new()
        {
            Name = "StrongVPN",
            Apps = ["strongvpn.exe"],
            Hosts = ["strongvpn.com"],
        },
        new()
        {
            Name = "IPVanish",
            Apps = ["ipvanish.exe"],
            Hosts = ["ipvanish.com"],
        },
        new()
        {
            Name = "Hide.me",
            Apps = ["hideme.exe"],
            Hosts = ["hide.me"],
        },
    ];

    public static int TotalApps => Entries.SelectMany(e => e.Apps).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public static int TotalHosts => Entries.SelectMany(e => e.Hosts).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    private static readonly HashSet<string> ManagedHosts = BuildManagedHostSet();

    public static bool IsManagedHost(string host)
    {
        var normalized = NormalizeHost(host);
        return normalized is not null && ManagedHosts.Contains(normalized);
    }

    private static HashSet<string> BuildManagedHostSet()
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            foreach (var host in entry.Hosts)
            {
                hosts.Add(NormalizeHost(host));
                hosts.Add(NormalizeHost($"www.{host}"));
            }
        }

        return hosts;
    }

    private static string NormalizeHost(string host)
    {
        var trimmed = host.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("www.", StringComparison.Ordinal))
            trimmed = trimmed[4..];

        return trimmed;
    }
}
