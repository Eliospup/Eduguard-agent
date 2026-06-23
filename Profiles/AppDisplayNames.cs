namespace EduGuardAgent.Profiles;

internal static class AppDisplayNames
{
    private static readonly Dictionary<string, string> VpnByExe = BuildVpnLookup();
    private static readonly Dictionary<string, string> SecurityToolByExe = new(StringComparer.OrdinalIgnoreCase)
    {
        ["taskmgr.exe"] = "Task Manager",
        ["regedit.exe"] = "Registry Editor",
        ["cmd.exe"] = "Command Prompt",
        ["powershell.exe"] = "PowerShell",
        ["pwsh.exe"] = "PowerShell",
        ["powershell_ise.exe"] = "PowerShell ISE",
        ["msconfig.exe"] = "System Configuration",
        ["mmc.exe"] = "Management Console",
        ["control.exe"] = "Control Panel",
        ["systemsettings.exe"] = "Windows Settings",
        ["procexp.exe"] = "Process Explorer",
        ["procexp64.exe"] = "Process Explorer",
        ["processhacker.exe"] = "Process Hacker",
        ["systeminformer.exe"] = "System Informer",
        ["procmon.exe"] = "Process Monitor",
        ["procmon64.exe"] = "Process Monitor",
    };

    public static string Resolve(string processName)
    {
        var normalized = NormalizeProcessName(processName);
        if (VpnByExe.TryGetValue(normalized, out var vpnName))
            return vpnName;
        if (SecurityToolByExe.TryGetValue(normalized, out var toolName))
            return toolName;

        return FriendlyExeName(normalized);
    }

    private static Dictionary<string, string> BuildVpnLookup()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in VpnBlocklist.Entries)
        {
            foreach (var app in entry.Apps)
                map[NormalizeProcessName(app)] = entry.Name;
        }

        return map;
    }

    private static string NormalizeProcessName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}.exe";
    }

    private static string FriendlyExeName(string exeName)
    {
        var baseName = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? exeName[..^4]
            : exeName;

        var words = baseName
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', words.Select(static word =>
            word.Length == 0
                ? word
                : char.ToUpperInvariant(word[0]) + word[1..]));
    }
}
