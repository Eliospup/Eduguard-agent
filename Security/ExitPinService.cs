using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

[SupportedOSPlatform("windows")]
internal sealed class ExitPinService
{
    // New PINs typed by a parent must be at least 6 digits: the verifier lives on a
    // user-readable disk, so a 4-digit PIN (10^4) is brute-forceable in seconds regardless
    // of the KDF. Server- and legacy-sourced PINs keep the historical 4-8 range so we
    // don't break already-provisioned devices.
    private static readonly Regex NewPinFormat = new(@"^[0-9]{6,8}$", RegexOptions.CultureInvariant);
    private static readonly Regex LegacyPinFormat = new(@"^[0-9]{4,8}$", RegexOptions.CultureInvariant);

    private const int MaxAttemptsBeforeLockout = 3;
    private const int BaseLockoutSeconds = 30;

    private readonly ExitPinStorage _storage = new();
    private readonly object _sync = new();

    // PBKDF2 verifier of the active PIN — never the PIN itself.
    private string? _verifier;
    private int _consecutiveFailures;
    private int _lockoutTier;
    private DateTimeOffset? _lockoutUntil;
    private int _pendingSuccesses;
    private int _pendingFailures;

    public bool IsRequired
    {
        get
        {
            lock (_sync)
                return !string.IsNullOrEmpty(_verifier);
        }
    }

    public void LoadFromStorage()
    {
        lock (_sync)
        {
            if (_storage.TryLoadVerifier(out var verifier))
            {
                _verifier = verifier;
                return;
            }

            // Migrate a legacy reversible PIN into the hashed store, then it stops existing
            // in recoverable form on disk.
            if (_storage.TryLoadLegacyPlaintext(out var legacy) && IsValidLegacyFormat(legacy))
            {
                _verifier = PinHasher.Hash(legacy);
                _storage.SaveVerifier(_verifier);
                AuditLog.Write("Exit PIN migrated to hashed storage.");
            }
        }
    }

    /// <summary>Hashes and persists a new PIN. Callers validate the format first.</summary>
    public void SetPin(string pin)
    {
        lock (_sync)
        {
            if (_verifier is not null && PinHasher.Verify(pin, _verifier))
                return; // Unchanged.

            _verifier = PinHasher.Hash(pin);
            _storage.SaveVerifier(_verifier);
            ResetLockoutLocked();
            AuditLog.Write("Exit PIN set/updated.");
        }
    }

    public void ClearPin()
    {
        lock (_sync)
        {
            if (_verifier is null)
                return;

            _verifier = null;
            _storage.Wipe();
            ResetLockoutLocked();
            AuditLog.Write("Exit PIN cleared.");
        }
    }

    /// <summary>Server-/catalog-sourced update. Accepts the legacy 4-8 digit range.</summary>
    public void UpdateFromServer(string? pin)
    {
        if (pin is null)
        {
            ClearPin();
            return;
        }

        if (!IsValidLegacyFormat(pin))
        {
            AuditLog.Write("Exit PIN rejected — invalid format from server.");
            return;
        }

        SetPin(pin);
    }

    public bool IsLockedOut(out TimeSpan remaining)
    {
        lock (_sync)
        {
            if (_lockoutUntil is null || DateTimeOffset.UtcNow >= _lockoutUntil)
            {
                remaining = TimeSpan.Zero;
                return false;
            }

            remaining = _lockoutUntil.Value - DateTimeOffset.UtcNow;
            return true;
        }
    }

    public bool TryVerify(string attempt, string context)
    {
        lock (_sync)
        {
            if (string.IsNullOrEmpty(_verifier))
                return true;

            if (IsLockedOut(out _))
            {
                AuditLog.Write($"Exit PIN lockout active — denied ({context}).");
                return false;
            }

            if (PinHasher.Verify(attempt, _verifier))
            {
                ResetLockoutLocked();
                _pendingSuccesses++;
                AuditLog.Write($"Exit PIN accepted ({context}).");
                return true;
            }

            _consecutiveFailures++;
            _pendingFailures++;
            AuditLog.Write($"Exit PIN rejected ({context}), attempt {_consecutiveFailures}.");

            if (_consecutiveFailures >= MaxAttemptsBeforeLockout)
            {
                _consecutiveFailures = 0;
                _lockoutTier++;
                var seconds = BaseLockoutSeconds * (1 << (_lockoutTier - 1));
                _lockoutUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
                AuditLog.Write($"Exit PIN lockout started for {seconds}s.");
            }

            return false;
        }
    }

    public void ApplyAuditTo(HeartbeatRequest request)
    {
        lock (_sync)
        {
            if (_pendingSuccesses == 0 && _pendingFailures == 0)
                return;

            request.ExitPinAudit = new ExitPinAuditPayload
            {
                Successes = _pendingSuccesses,
                Failures = _pendingFailures,
            };

            _pendingSuccesses = 0;
            _pendingFailures = 0;
        }
    }

    private void ResetLockoutLocked()
    {
        _consecutiveFailures = 0;
        _lockoutTier = 0;
        _lockoutUntil = null;
    }

    /// <summary>Format required for a brand-new PIN typed by a parent (stronger minimum).</summary>
    public static bool IsValidFormat(string? pin) =>
        !string.IsNullOrEmpty(pin) && NewPinFormat.IsMatch(pin);

    /// <summary>Format accepted for migration and server-provisioned PINs (historical range).</summary>
    public static bool IsValidLegacyFormat(string? pin) =>
        !string.IsNullOrEmpty(pin) && LegacyPinFormat.IsMatch(pin);
}
