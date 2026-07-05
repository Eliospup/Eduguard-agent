using System.Globalization;
using System.Runtime.Versioning;

namespace EduGuardAgent.Security;

/// <summary>
/// Persists a user-initiated "self lock" expiry — a UTC instant until which the exit PIN is
/// intentionally disabled so a solo user cannot let themselves out early. The value is wrapped
/// with <see cref="StateProtection"/> (encrypted + tamper-evident) and lives in the protected
/// folder, so it survives restarts and casual edits.
///
/// Fails OPEN (no lock) on a missing, unreadable, or tampered file: unlike the security flags,
/// a corrupt self-lock file must never brick the machine forever — the worst case of a lost
/// file is that the user regains their PIN early, which is acceptable for a self-imposed lock.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SelfLockStore
{
    private readonly string _path;

    public SelfLockStore()
    {
        _path = SecureDataPaths.PathFor(Config.SelfLockFileName);
    }

    /// <summary>The stored expiry instant, or null when no readable value exists.</summary>
    public DateTimeOffset? LoadUntil()
    {
        var status = StateProtection.TryRead(_path, out var content);
        if (status != StateReadStatus.Ok)
            return null;

        if (DateTimeOffset.TryParse(
                content.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var until))
        {
            return until.ToUniversalTime();
        }

        return null;
    }

    public void Save(DateTimeOffset until) =>
        StateProtection.Write(_path, until.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));

    public void Clear() => StateProtection.Delete(_path);
}
