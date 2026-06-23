using System.Diagnostics;
using System.Runtime.Versioning;

namespace EduGuardAgent.Security;

internal readonly record struct BlockedProcessKiller(int ProcessId, string Label);

/// <summary>
/// Blocks lightweight "automatic process killer" utilities (e.g.
/// <c>pk.exe</c> from automatic-process-killer) that terminate Guardi by
/// executable name on a tight loop.
///
/// Name-based blocking alone is bypassed by renaming the binary, so we also
/// match suspicious image paths and file names. Gated by
/// <see cref="SecurityRuntimeFlags.ShouldBlockProcessKillers"/> (local/Dom toggle).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessKillerDefense
{
    /// <summary>Exact image file names (with .exe).</summary>
    private static readonly string[] KnownExecutables =
    [
        // automatic-process-killer (DeliciousLines + sm18lr88 fork) — pk.exe
        "pk.exe",
        "apk.exe",

        // Auto Kill Any Process (AKMA Solutions) — hitlist / auto-kill loop
        "autokillanyprocess.exe",

        // Automatically Kill Processes Software (Sobolsoft)
        "automaticallykillprocess.exe",

        // KillProcess (TGMDev) — "Scan and Kill" scheduler
        "killprocess.exe",

        // ProcessKO (SoftwareOK) — timer / favorite KO auto-close
        "processko.exe",
        "processko_x64.exe",

        // Advanced Process Blocker (EthernalStar) — block by name, watch & kill
        "advanced-process-blocker.exe",
        "advancedprocessblocker.exe",

        // Parental-control / family-safety bypass killers (GitHub)
        "familycontrolkiller.exe",
        "msfamdisable.exe",
        "wpckiller.exe",
        "wpckillerfsm.exe",
        "wpcmondisabler.exe",
        "wpcmon-disabler.exe",
        "stopfamily.exe",
        "stop-family.exe",
        "programcrasher.exe",
        "parentalcontrolscrasher.exe",
        "parental-controls-killer.exe",
        "parental-controls-crasher.exe",

        // Generic / historical auto-kill utilities
        "processkiller.exe",
        "prockill.exe",
        "autopkill.exe",
        "autoprocesskiller.exe",
        "automaticprocesskiller.exe",
        "superkill.exe",
        "processassassin.exe",
        "taskkiller.exe",

        // Tray tools configurable to kill arbitrary exe lists
        "killswitch.exe",

        // Sysinternals CLI — often scripted in auto-kill loops
        "pskill.exe",
        "pskill64.exe",

        // Alternative task managers with auto-kill / timer features
        "dtaskmanager.exe",
    ];

    /// <summary>
    /// <see cref="Process.ProcessName"/> values for spaced distribution builds
    /// (no .exe suffix).
    /// </summary>
    private static readonly string[] KnownProcessStems =
    [
        "Automatically Kill Process",
        "Auto Kill Any Process",
        "Advanced Process Blocker",
        "Parental Controls Crasher",
        "Program Crasher",
        "WPC Killer",
        "Family Control Killer",
        "FamilySafetyMonitor",
        "TimeToKill",
    ];

    private static readonly string[] ImagePathMarkers =
    [
        // automatic-process-killer forks
        "automatic-process-killer",
        "automatic_process_killer",
        "automaticprocesskiller",
        "Win32ProcessKiller",
        "deliciouslines",
        "sm18lr88",

        // Download / vendor distributions
        "auto-kill-any-process",
        "autokillanyprocess",
        "automatically-kill-process",
        "automaticallykillprocess",
        "sobolsoft",
        "akma",
        "tgmdev",
        "killprocess",
        "processko",
        "softwareok",

        // GitHub bypass / auto-kill projects
        "advanced-process-blocker",
        "advancedprocessblocker",
        "ethernalstar",
        "program-crasher",
        "programcrasher",
        "parental-controls-killer",
        "parental-controls-crasher",
        "parentalcontrolscrasher",
        "glur-dev",
        "familycontrolkiller",
        "family-control-killer",
        "chomisiowiecgamer",
        "msfamdisable",
        "gametec-live",
        "winpcl-bypass",
        "winpcl",
        "wpckiller",
        "wpcmon-disabler",
        "wpcmondisabler",
        "stop-family",
        "stopfamily",
        "timetokill",
        "time-to-kill",
        "sysmaid",
        "killswitch",
        "dtaskmanager",

        // Generic path fragments
        "process-killer",
        "process_killer",
        "processkiller",
    ];

    private static readonly string[] ExecutableNameMarkers =
    [
        "processkiller",
        "process-killer",
        "process_killer",
        "prockill",
        "killprocess",
        "autoprocesskill",
        "automaticprocesskill",
        "processblocker",
        "process-blocker",
        "advancedprocessblocker",
        "programcrasher",
        "parentalcontrolkiller",
        "parentalcontrolscrasher",
        "familycontrolkiller",
        "wpckiller",
        "wpcmondisabler",
        "msfamdisable",
        "stopfamily",
        "autokillany",
        "timetokill",
        "killswitch",
        "taskkiller",
        "superkill",
        "processassassin",
    ];

    // process.MainModule reads the PE header and can throw on protected processes — one of the
    // most expensive Process APIs. Caching the per-PID image-path verdict means this only runs
    // once per process lifetime instead of on every 400ms enforcement tick, which was otherwise
    // saturating a CPU core checking the same few hundred long-lived processes repeatedly.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> ImagePathVerdictCache = new();

    /// <summary>
    /// Terminates hostile process-killer tools. Returns blocked process ids and labels.
    /// </summary>
    public static IReadOnlyList<BlockedProcessKiller> Enforce(int selfPid)
    {
        var blocked = new List<BlockedProcessKiller>();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return blocked;
        }

        var currentPids = new HashSet<int>(processes.Length);

        foreach (var process in processes)
        {
            try
            {
                currentPids.Add(process.Id);

                if (process.Id == selfPid)
                    continue;

                if (!TryClassify(process, out var label))
                    continue;

                var pid = process.Id;
                process.Kill(entireProcessTree: true);
                blocked.Add(new BlockedProcessKiller(pid, label));
            }
            catch
            {
                // Elevated killers may win briefly — the watchdog keeps Guardi alive.
            }
            finally
            {
                process.Dispose();
            }
        }

        PruneImagePathCache(currentPids);
        return blocked;
    }

    private static void PruneImagePathCache(HashSet<int> currentPids)
    {
        foreach (var pid in ImagePathVerdictCache.Keys)
        {
            if (!currentPids.Contains(pid))
                ImagePathVerdictCache.TryRemove(pid, out _);
        }
    }

    private static bool TryClassify(Process process, out string label)
    {
        label = string.Empty;

        string processName;
        try
        {
            processName = process.ProcessName;
            if (string.IsNullOrWhiteSpace(processName))
                return false;
        }
        catch
        {
            return false;
        }

        var exe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : $"{processName}.exe";

        if (IsKnownProcess(processName, exe))
        {
            label = exe;
            return true;
        }

        if (MatchesExecutableNameMarker(exe))
        {
            label = exe;
            return true;
        }

        if (ImagePathVerdictCache.TryGetValue(process.Id, out var cachedVerdict))
        {
            if (cachedVerdict)
                label = exe;
            return cachedVerdict;
        }

        var imagePath = TryGetImagePath(process);
        var matches = imagePath is not null && MatchesImagePath(imagePath);
        ImagePathVerdictCache[process.Id] = matches;

        if (matches)
            label = exe;

        return matches;
    }

    private static bool IsKnownProcess(string processName, string exe) =>
        KnownExecutables.Any(known => string.Equals(known, exe, StringComparison.OrdinalIgnoreCase))
        || KnownProcessStems.Any(stem => string.Equals(stem, processName, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesExecutableNameMarker(string exe)
    {
        var stem = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? exe[..^4]
            : exe;

        return ExecutableNameMarkers.Any(marker =>
            stem.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesImagePath(string imagePath)
    {
        var normalized = imagePath.Replace('/', '\\');
        return ImagePathMarkers.Any(marker =>
            normalized.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryGetImagePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
