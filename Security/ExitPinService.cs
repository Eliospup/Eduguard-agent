using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

[SupportedOSPlatform("windows")]
internal sealed class ExitPinService
{
    private static readonly Regex PinFormat = new(@"^[0-9]{4,8}$", RegexOptions.CultureInvariant);

    private const int MaxAttemptsBeforeLockout = 3;
    private const int BaseLockoutSeconds = 30;

    private readonly ExitPinStorage _storage = new();
    private readonly object _sync = new();

    private string? _pin;
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
                return !string.IsNullOrEmpty(_pin);
        }
    }

    public bool TryGetActivePin(out string pin)
    {
        lock (_sync)
        {
            if (string.IsNullOrEmpty(_pin))
            {
                pin = string.Empty;
                return false;
            }

            pin = _pin;
            return true;
        }
    }

    public void LoadFromStorage()
    {
        lock (_sync)
        {
            if (_storage.TryLoad(out var cached) && IsValidFormat(cached))
                _pin = cached;
        }
    }

    public void UpdateFromServer(string? pin)
    {
        lock (_sync)
        {
            if (pin is null)
            {
                _pin = null;
                _storage.Wipe();
                AuditLog.Write("Exit PIN cleared by server.");
                return;
            }

            if (!IsValidFormat(pin))
            {
                AuditLog.Write("Exit PIN rejected — invalid format from server.");
                return;
            }

            if (string.Equals(_pin, pin, StringComparison.Ordinal))
                return;

            _pin = pin;
            _storage.Save(pin);
            AuditLog.Write("Exit PIN updated from server.");
        }
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
            if (!IsRequired)
                return true;

            if (IsLockedOut(out _))
            {
                AuditLog.Write($"Exit PIN lockout active — denied ({context}).");
                return false;
            }

            if (string.Equals(attempt, _pin, StringComparison.Ordinal))
            {
                _consecutiveFailures = 0;
                _lockoutTier = 0;
                _lockoutUntil = null;
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

    public static bool IsValidFormat(string? pin) =>
        !string.IsNullOrEmpty(pin) && PinFormat.IsMatch(pin);
}
