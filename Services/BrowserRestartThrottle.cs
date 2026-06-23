namespace EduGuardAgent.Services;

/// <summary>
/// Per-browser cooldown so Guardi does not restart Chrome/Firefox in a tight loop.
/// </summary>
internal static class BrowserRestartThrottle
{
    private static readonly object Lock = new();
    private static readonly Dictionary<BrowserKind, DateTimeOffset> LastRestartAt = new();
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(90);

    public static bool ShouldRestart(BrowserKind kind)
    {
        lock (Lock)
        {
            if (!LastRestartAt.TryGetValue(kind, out var last))
                return true;

            return DateTimeOffset.UtcNow - last >= Cooldown;
        }
    }

    public static void MarkRestarted(BrowserKind kind)
    {
        lock (Lock)
            LastRestartAt[kind] = DateTimeOffset.UtcNow;
    }

    public static void Reset(BrowserKind? kind = null)
    {
        lock (Lock)
        {
            if (kind is null)
                LastRestartAt.Clear();
            else
                LastRestartAt.Remove(kind.Value);
        }
    }
}
