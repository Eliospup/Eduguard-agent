using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Nodes;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Adaptive adult-content filtering at the DNS level. When enabled, system DNS is switched
/// to Cloudflare Family (1.1.1.3 — malware + adult), whose professionally maintained,
/// continuously updated categorisation covers the long tail (millions of domains) that no
/// hand-curated list can. Resolution latency is on par with regular DNS, so browsing speed
/// is unaffected, and nothing is blocked beyond the resolver's adult/malware categories.
///
/// Reversibility: the previous per-adapter DNS configuration (static servers or DHCP) is
/// backed up before the switch and restored on release — including after a crash, via
/// <see cref="TryReleaseOrphanedState"/> on startup when the shield should be off.
///
/// Hardening (threat model: the supervised user actively tries to bypass):
///  - a reassert timer re-applies the DNS servers if they are changed behind our back;
///  - browser DoH is locked off via policy (Chromium registry + Firefox policies.json),
///    otherwise the browser would tunnel DNS over HTTPS straight past the family resolver.
/// </summary>
internal sealed class FamilyDnsShieldService
{
    private static readonly TimeSpan ReassertInterval = TimeSpan.FromSeconds(30);

    private static readonly string[] FamilyV4 = ["1.1.1.3", "1.0.0.3"];
    private static readonly string[] FamilyV6 = ["2606:4700:4700::1113", "2606:4700:4700::1003"];

    private static readonly string BackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "dns_shield_backup.json");

    private static readonly (string Hive, string Vendor)[] ChromiumPolicyRoots =
    [
        (@"SOFTWARE\Policies\Google\Chrome", "Chrome"),
        (@"SOFTWARE\Policies\Microsoft\Edge", "Edge"),
        (@"SOFTWARE\Policies\BraveSoftware\Brave", "Brave"),
    ];

    private readonly object _gate = new();
    private Timer? _reassertTimer;
    private bool _enabled;

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
                return _enabled;
        }
    }

    public string? LastError { get; private set; }

    public void Enable()
    {
        lock (_gate)
        {
            if (!HostsFileManager.IsRunningAsAdmin())
            {
                LastError = "Administrator rights are required to enable the family DNS shield.";
                AuditLog.Write(LastError);
                return;
            }

            try
            {
                var adapters = GetActiveAdapters();
                if (adapters.Count == 0)
                {
                    LastError = "No active network adapter found for the family DNS shield.";
                    return;
                }

                // Only snapshot the pre-shield configuration once — re-enabling while
                // already active must not overwrite the true original with our own servers.
                if (!File.Exists(BackupPath))
                    SaveBackup(adapters);

                foreach (var adapter in adapters)
                    ApplyFamilyDns(adapter.Name);

                ApplyDohLock();
                FlushDnsCache();

                _enabled = true;
                LastError = null;
                _reassertTimer ??= new Timer(_ => Reassert(), null, ReassertInterval, ReassertInterval);
                AuditLog.Write($"Family DNS shield enabled on {adapters.Count} adapter(s) (Cloudflare Family).");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AuditLog.Write($"Family DNS shield enable failed: {ex.Message}");
            }
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _reassertTimer?.Dispose();
            _reassertTimer = null;
            _enabled = false;

            if (!File.Exists(BackupPath))
            {
                // Never enabled (or already fully released) — still strip DoH policies in
                // case a previous session crashed between the two steps.
                RemoveDohLock();
                return;
            }

            try
            {
                var backup = LoadBackup();
                foreach (var entry in backup)
                    RestoreAdapterDns(entry);

                RemoveDohLock();
                FlushDnsCache();
                File.Delete(BackupPath);
                AuditLog.Write("Family DNS shield released — previous DNS configuration restored.");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                AuditLog.Write($"Family DNS shield release failed (will retry on next startup): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Startup safety net: if a backup file exists but the shield should not be active
    /// (crash mid-session), restore the original DNS configuration.
    /// </summary>
    public void TryReleaseOrphanedState()
    {
        if (File.Exists(BackupPath))
        {
            AuditLog.Write("Releasing orphaned family DNS shield state from a previous session.");
            Release();
        }
    }

    private void Reassert()
    {
        // Reassert only nudges drifted adapters; it must never throw on a background timer.
        try
        {
            lock (_gate)
            {
                if (!_enabled)
                    return;

                foreach (var adapter in GetActiveAdapters())
                {
                    var current = ReadRegistryNameServer("Tcpip", adapter.Id);
                    if (!string.Equals(current, string.Join(",", FamilyV4), StringComparison.Ordinal))
                    {
                        AuditLog.Write($"SECURITY: family DNS drift on '{adapter.Name}' — re-applying.");
                        ApplyFamilyDns(adapter.Name);
                    }
                }
            }
        }
        catch
        {
            // Next tick retries.
        }
    }

    // --- DNS plumbing ---------------------------------------------------------------------

    private sealed record AdapterInfo(string Id, string Name);

    private sealed class AdapterBackup
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Registry NameServer value; empty string means DHCP-assigned DNS.</summary>
        public string V4 { get; set; } = string.Empty;
        public string V6 { get; set; } = string.Empty;
    }

    private static List<AdapterInfo> GetActiveAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                && ni.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && ni.Supports(NetworkInterfaceComponent.IPv4))
            .Select(ni => new AdapterInfo(ni.Id, ni.Name))
            .ToList();

    private static void SaveBackup(List<AdapterInfo> adapters)
    {
        var entries = adapters.Select(a => new AdapterBackup
        {
            Id = a.Id,
            Name = a.Name,
            V4 = ReadRegistryNameServer("Tcpip", a.Id),
            V6 = ReadRegistryNameServer("Tcpip6", a.Id),
        }).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(entries));
    }

    private static List<AdapterBackup> LoadBackup()
    {
        try
        {
            return JsonSerializer.Deserialize<List<AdapterBackup>>(File.ReadAllText(BackupPath)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string ReadRegistryNameServer(string stack, string adapterId)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            $@"SYSTEM\CurrentControlSet\Services\{stack}\Parameters\Interfaces\{adapterId}");
        return key?.GetValue("NameServer") as string ?? string.Empty;
    }

    private static void ApplyFamilyDns(string adapterName)
    {
        SetStaticDns("ipv4", adapterName, FamilyV4);
        SetStaticDns("ipv6", adapterName, FamilyV6);
    }

    private static void RestoreAdapterDns(AdapterBackup entry)
    {
        RestoreStack("ipv4", entry.Name, entry.V4);
        RestoreStack("ipv6", entry.Name, entry.V6);
    }

    private static void RestoreStack(string stack, string adapterName, string savedNameServer)
    {
        if (string.IsNullOrWhiteSpace(savedNameServer))
        {
            RunNetsh($"interface {stack} set dnsservers name=\"{adapterName}\" source=dhcp");
            return;
        }

        var servers = savedNameServer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        SetStaticDns(stack, adapterName, servers);
    }

    private static void SetStaticDns(string stack, string adapterName, IReadOnlyList<string> servers)
    {
        if (servers.Count == 0)
            return;

        RunNetsh($"interface {stack} set dnsservers name=\"{adapterName}\" static {servers[0]} primary validate=no");
        for (var i = 1; i < servers.Count; i++)
            RunNetsh($"interface {stack} add dnsservers name=\"{adapterName}\" {servers[i]} index={i + 1} validate=no");
    }

    private static void RunNetsh(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        process?.WaitForExit(10000);
    }

    private static void FlushDnsCache()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
            })?.Dispose();
        }
        catch
        {
            // Best effort.
        }
    }

    // --- Browser DoH lockdown ---------------------------------------------------------------
    // Without this, browsers resolve names over DNS-over-HTTPS and never consult the family
    // resolver. Chromium's policy is a registry value; Firefox uses policies.json (merged so
    // the extension-install and SafeSearch blocks written by other services are preserved).

    // Well-known public DoH/DoT resolver IPs. Blocking outbound 443/853 to these forces any
    // browser or app with hardcoded resolver IPs to fall back to the system resolver (our
    // family DNS) instead of tunnelling DNS past it. Deliberately EXCLUDES 1.1.1.3 / 1.0.0.3
    // (the Cloudflare Family resolver we route to). Not exhaustive — CDN-fronted DoH endpoints
    // (e.g. mozilla.cloudflare-dns.com on shared Cloudflare IPs) can't be IP-blocked without
    // breaking legitimate sites; those are covered by the per-browser DoH policy + the
    // unsupported-browser block instead.
    private const string DohFirewallRuleTcp4 = "Guardi DoH block v4 (TCP)";
    private const string DohFirewallRuleUdp4 = "Guardi DoH block v4 (UDP)";
    private const string DohFirewallRuleTcp6 = "Guardi DoH block v6 (TCP)";
    private const string DohFirewallRuleUdp6 = "Guardi DoH block v6 (UDP)";

    private static readonly string[] DohResolverIpv4 =
    [
        // Cloudflare (public + malware-only; family 1.1.1.3/1.0.0.3 intentionally kept open)
        "1.1.1.1", "1.0.0.1", "1.1.1.2", "1.0.0.2",
        // Google Public DNS
        "8.8.8.8", "8.8.4.4",
        // Quad9
        "9.9.9.9", "149.112.112.112", "9.9.9.11", "149.112.112.11",
        // OpenDNS / Cisco
        "208.67.222.222", "208.67.220.220", "208.67.222.123", "208.67.220.123",
        // AdGuard DNS
        "94.140.14.14", "94.140.15.15", "94.140.14.15", "94.140.15.16",
        // CleanBrowsing
        "185.228.168.9", "185.228.169.9", "185.228.168.10", "185.228.169.11",
        // Mullvad
        "194.242.2.2", "194.242.2.4", "194.242.2.9",
        // NextDNS anycast
        "45.90.28.0", "45.90.30.0",
        // ControlD
        "76.76.2.0", "76.76.10.0",
        // DNS.SB
        "185.222.222.222", "45.11.45.11",
    ];

    private static readonly string[] DohResolverIpv6 =
    [
        "2606:4700:4700::1111", "2606:4700:4700::1001",        // Cloudflare
        "2001:4860:4860::8888", "2001:4860:4860::8844",        // Google
        "2620:fe::fe", "2620:fe::9",                            // Quad9
    ];

    private static void ApplyDohLock()
    {
        ApplyDohFirewallBlock();

        foreach (var (root, _) in ChromiumPolicyRoots)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(root);
                key.SetValue("DnsOverHttpsMode", "off");
            }
            catch
            {
                // Browser not installed or key locked — non-fatal.
            }
        }

        foreach (var installRoot in FirefoxInstallRoots.All())
        {
            try
            {
                var policyPath = FirefoxInstallRoots.PolicyPath(installRoot);
                var root = FirefoxInstallRoots.ReadOrCreate(policyPath);
                var policies = FirefoxInstallRoots.EnsureObject(root, "policies");
                policies["DNSOverHTTPS"] = new JsonObject
                {
                    ["Enabled"] = false,
                    ["Locked"] = true,
                };
                FirefoxInstallRoots.WriteIndented(policyPath, root);
            }
            catch
            {
                // Non-fatal; DNS-level filtering still applies to OS resolution.
            }
        }
    }

    private static void ApplyDohFirewallBlock()
    {
        // Delete first so re-enabling doesn't stack duplicate rules (netsh allows same-name rules).
        RemoveDohFirewallBlock();

        var v4 = string.Join(",", DohResolverIpv4);
        var v6 = string.Join(",", DohResolverIpv6);
        // DoH is TCP/UDP 443; DoT is TCP 853. v4 and v6 are separate rules so a mixed-family
        // remoteip list can't get the whole rule rejected on stricter Windows builds.
        RunNetsh($"advfirewall firewall add rule name=\"{DohFirewallRuleTcp4}\" dir=out action=block protocol=TCP remoteport=443,853 remoteip={v4}");
        RunNetsh($"advfirewall firewall add rule name=\"{DohFirewallRuleUdp4}\" dir=out action=block protocol=UDP remoteport=443 remoteip={v4}");
        RunNetsh($"advfirewall firewall add rule name=\"{DohFirewallRuleTcp6}\" dir=out action=block protocol=TCP remoteport=443,853 remoteip={v6}");
        RunNetsh($"advfirewall firewall add rule name=\"{DohFirewallRuleUdp6}\" dir=out action=block protocol=UDP remoteport=443 remoteip={v6}");
    }

    /// <summary>Removes the DoH-resolver firewall rules. Safe to call when they don't exist.</summary>
    public static void RemoveDohFirewallBlock()
    {
        foreach (var name in new[] { DohFirewallRuleTcp4, DohFirewallRuleUdp4, DohFirewallRuleTcp6, DohFirewallRuleUdp6 })
            RunNetsh($"advfirewall firewall delete rule name=\"{name}\"");
    }

    private static void RemoveDohLock()
    {
        RemoveDohFirewallBlock();

        foreach (var (root, _) in ChromiumPolicyRoots)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(root, writable: true);
                key?.DeleteValue("DnsOverHttpsMode", throwOnMissingValue: false);
            }
            catch
            {
                // Non-fatal.
            }
        }

        foreach (var installRoot in FirefoxInstallRoots.All())
        {
            try
            {
                var policyPath = FirefoxInstallRoots.PolicyPath(installRoot);
                if (!File.Exists(policyPath))
                    continue;

                if (JsonNode.Parse(File.ReadAllText(policyPath)) is not JsonObject root
                    || root["policies"] is not JsonObject policies)
                    continue;

                if (policies.Remove("DNSOverHTTPS"))
                    FirefoxInstallRoots.WriteIndented(policyPath, root);
            }
            catch
            {
                // Non-fatal.
            }
        }
    }
}
