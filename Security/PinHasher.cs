using System.Security.Cryptography;
using System.Text;

namespace EduGuardAgent.Security;

/// <summary>
/// One-way verifier for the exit PIN. Only a salted PBKDF2 hash is ever stored, so a
/// same-account user cannot recover the PIN by reading it off disk (the previous design
/// stored a reversible DPAPI blob).
///
/// Honest limit: because the verifier lives on a user-readable disk, a short PIN is still
/// offline-brute-forceable (10^n guesses x KDF cost). That's why the local UI enforces a
/// longer minimum for newly-set PINs and we use a high iteration count. Making the
/// verifier un-brute-forceable by the supervised user requires holding it under a
/// principal they don't control (a SYSTEM service).
/// </summary>
internal static class PinHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 600_000; // OWASP PBKDF2-SHA256 guidance.
    private const string Scheme = "pbkdf2-sha256";

    public static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string verifier)
    {
        try
        {
            var parts = verifier.Split('$');
            if (parts.Length != 4 || parts[0] != Scheme)
                return false;

            var iterations = int.Parse(parts[1]);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public static bool LooksLikeVerifier(string value) =>
        value.StartsWith(Scheme + "$", StringComparison.Ordinal);
}
