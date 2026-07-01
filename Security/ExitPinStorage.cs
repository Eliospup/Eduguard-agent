using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace EduGuardAgent.Security;

/// <summary>
/// Persists the exit PIN as a one-way PBKDF2 verifier (never the recoverable PIN). The
/// verifier file is wrapped with <see cref="StateProtection"/> so an edit is detected as
/// tampering on read. A one-time migration recovers the legacy reversible DPAPI blob.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ExitPinStorage
{
    private readonly string _pinPath;

    public ExitPinStorage()
    {
        _pinPath = SecureDataPaths.PathFor(Config.ExitPinFileName);
    }

    /// <summary>Loads the stored PBKDF2 verifier. Returns false if absent or unreadable.</summary>
    public bool TryLoadVerifier(out string verifier)
    {
        verifier = string.Empty;

        var status = StateProtection.TryRead(_pinPath, out var content);
        if (status != StateReadStatus.Ok)
            return false;

        if (!PinHasher.LooksLikeVerifier(content))
            return false;

        verifier = content;
        return true;
    }

    public void SaveVerifier(string verifier) => StateProtection.Write(_pinPath, verifier);

    /// <summary>
    /// One-time migration: older builds stored the PIN as a reversible
    /// DPAPI(CurrentUser) blob. Recover it once so the caller can re-hash it.
    /// </summary>
    public bool TryLoadLegacyPlaintext(out string pin)
    {
        pin = string.Empty;

        if (!File.Exists(_pinPath))
            return false;

        try
        {
            var bytes = File.ReadAllBytes(_pinPath);

            // The new format is UTF-8 text beginning "EGP1:"; never treat it as legacy.
            if (bytes.Length >= 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "EGP1:")
                return false;

            var plain = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            pin = Encoding.UTF8.GetString(plain);
            return !string.IsNullOrEmpty(pin);
        }
        catch
        {
            return false;
        }
    }

    public void Wipe() => StateProtection.Delete(_pinPath);
}
