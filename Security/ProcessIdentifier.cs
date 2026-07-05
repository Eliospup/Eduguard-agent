using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace EduGuardAgent.Security;

/// <summary>
/// Identifies processes by their PE metadata (OriginalFilename / InternalName) rather
/// than just the running process name.  Windows preserves these version-info fields
/// even when the executable is renamed, so a supervised user who copies cmd.exe to
/// homework.exe is still detected.
///
/// Results are cached per PID+creation-time so the expensive FileVersionInfo read
/// happens at most once per process lifetime.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessIdentifier
{
    private readonly record struct CacheKey(int Pid, long StartTimeTicks);

    private static readonly ConcurrentDictionary<CacheKey, string?> Cache = new();

    /// <summary>
    /// Returns the PE OriginalFilename (with .exe) for the given process, or null
    /// if it cannot be read.  Results are cached per process lifetime.
    /// </summary>
    public static string? GetOriginalFilename(Process process)
    {
        var key = MakeKey(process);
        if (key is null)
            return null;

        return Cache.GetOrAdd(key.Value, _ => ReadOriginalFilename(process));
    }

    /// <summary>
    /// Removes cache entries for processes that are no longer alive.
    /// Call periodically from the enforcement tick to keep memory bounded.
    /// </summary>
    public static void PruneCache(HashSet<int> alivePids)
    {
        foreach (var key in Cache.Keys)
        {
            if (!alivePids.Contains(key.Pid))
                Cache.TryRemove(key, out _);
        }
    }

    private static CacheKey? MakeKey(Process process)
    {
        try
        {
            return new CacheKey(process.Id, process.StartTime.Ticks);
        }
        catch
        {
            // StartTime may throw for protected/system processes.
            // Fall back to PID-only (rare false cache hit if PID is recycled).
            try { return new CacheKey(process.Id, 0); }
            catch { return null; }
        }
    }

    private static string? ReadOriginalFilename(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (path is null)
                return null;

            var info = FileVersionInfo.GetVersionInfo(path);

            // Prefer OriginalFilename (most reliable), fall back to InternalName.
            var original = info.OriginalFilename;
            if (!string.IsNullOrWhiteSpace(original))
                return NormalizeExeName(original);

            var internalName = info.InternalName;
            if (!string.IsNullOrWhiteSpace(internalName))
                return NormalizeExeName(internalName);

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeExeName(string name)
    {
        var trimmed = name.Trim();
        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            trimmed += ".exe";

        return trimmed;
    }
}
