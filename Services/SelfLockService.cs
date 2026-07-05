using System.Collections.Generic;
using System.Runtime.Versioning;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// A user-initiated commitment lock ("self lock"). While active, every PIN-gated action is
/// refused — the UI shows the current mode's mascot instead of the PIN pad — until a fixed
/// expiry the user chose up front. There is intentionally NO early unlock: that is the whole
/// point for someone using the app on themselves. The expiry is persisted (tamper-evident) so
/// it survives restarts.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SelfLockService
{
    // A mis-click shouldn't arm a lock that's instantly "expired"; a fat-fingered duration
    // shouldn't be able to brick the machine for years either.
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(90);

    private readonly SelfLockStore _store = new();
    private readonly object _sync = new();
    private DateTimeOffset? _until;

    public void LoadFromStorage()
    {
        lock (_sync)
            _until = _store.LoadUntil();
    }

    public bool IsActive
    {
        get
        {
            lock (_sync)
                return _until is { } until && DateTimeOffset.UtcNow < until;
        }
    }

    /// <summary>The active expiry, or null when no unexpired lock is armed.</summary>
    public DateTimeOffset? ActiveUntil
    {
        get
        {
            lock (_sync)
                return _until is { } until && DateTimeOffset.UtcNow < until ? until : null;
        }
    }

    public TimeSpan Remaining
    {
        get
        {
            lock (_sync)
            {
                if (_until is { } until && DateTimeOffset.UtcNow < until)
                    return until - DateTimeOffset.UtcNow;
                return TimeSpan.Zero;
            }
        }
    }

    /// <summary>Clamps <paramref name="duration"/> to [MinDuration, MaxDuration], arms the lock, and returns the effective expiry.</summary>
    public DateTimeOffset Activate(TimeSpan duration)
    {
        var clamped =
            duration < MinDuration ? MinDuration :
            duration > MaxDuration ? MaxDuration :
            duration;

        var until = DateTimeOffset.UtcNow + clamped;
        lock (_sync)
        {
            _until = until;
            _store.Save(until);
        }

        AuditLog.Write($"Self-lock engaged until {until.ToLocalTime():g} — exit PIN disabled by the user's own choice.");
        return until;
    }

    /// <summary>Human-friendly "2 days 3 hours 15 minutes" for the remaining time.</summary>
    public static string Describe(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        var days = (int)span.TotalDays;
        var parts = new List<string>();
        if (days > 0)
            parts.Add($"{days} day{(days == 1 ? "" : "s")}");
        if (span.Hours > 0)
            parts.Add($"{span.Hours} hour{(span.Hours == 1 ? "" : "s")}");
        if (span.Minutes > 0)
            parts.Add($"{span.Minutes} minute{(span.Minutes == 1 ? "" : "s")}");

        return parts.Count > 0 ? string.Join(" ", parts) : "less than a minute";
    }
}
