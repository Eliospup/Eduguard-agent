using System.Security.Principal;
using System.Text;

namespace EduGuardAgent.Services;

internal sealed class HostsFileManager
{
    private const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string BeginMarker = "# BEGIN EDUGUARD";
    private const string EndMarker = "# END EDUGUARD";

    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public HostsUpdateResult ApplyBlocklist(IEnumerable<string> hosts)
    {
        if (!IsRunningAsAdmin())
        {
            return new HostsUpdateResult(
                false,
                "Administrator rights are required to block websites on this PC.");
        }

        try
        {
            var lines = File.Exists(HostsPath)
                ? File.ReadAllLines(HostsPath).ToList()
                : [];

            RemoveEduGuardSection(lines);

            var entries = hosts
                .SelectMany(UrlBlocklistStore.ExpandHostEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(h => h)
                .ToList();

            if (entries.Count > 0)
            {
                lines.Add(BeginMarker);
                lines.Add("# Managed by EduGuard — do not edit this section");
                foreach (var host in entries)
                {
                    lines.Add($"127.0.0.1 {host}");
                    lines.Add($"::1 {host}");
                }
                lines.Add(EndMarker);
            }

            File.WriteAllLines(HostsPath, lines, Encoding.UTF8);
            TryFlushDnsCache();
            return new HostsUpdateResult(true, null);
        }
        catch (Exception ex)
        {
            return new HostsUpdateResult(false, ex.Message);
        }
    }

    public HostsUpdateResult Clear()
    {
        return ApplyBlocklist([]);
    }

    public IReadOnlyList<string> ReadManagedHosts()
    {
        if (!File.Exists(HostsPath))
            return [];

        var lines = File.ReadAllLines(HostsPath);
        var begin = Array.FindIndex(lines, l => l.Trim() == BeginMarker);
        if (begin < 0)
            return [];

        var end = -1;
        for (var i = begin + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == EndMarker)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
            return [];

        var hosts = new List<string>();

        for (var i = begin + 1; i < end; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && (parts[0] == "127.0.0.1" || parts[0] == "::1"))
            {
                hosts.Add(parts[1]);
            }
        }

        return hosts;
    }

    private static void RemoveEduGuardSection(List<string> lines)
    {
        var begin = lines.FindIndex(l => l.Trim() == BeginMarker);
        if (begin < 0)
            return;

        var end = lines.FindIndex(begin, l => l.Trim() == EndMarker);
        if (end < 0)
            return;

        lines.RemoveRange(begin, end - begin + 1);
        while (begin < lines.Count && string.IsNullOrWhiteSpace(lines[begin]))
            lines.RemoveAt(begin);
    }

    private static void TryFlushDnsCache()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(5000);
        }
        catch
        {
            // Best effort.
        }
    }
}

internal readonly record struct HostsUpdateResult(bool Success, string? Error);
