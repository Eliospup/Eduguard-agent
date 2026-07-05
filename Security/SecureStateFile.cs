namespace EduGuardAgent.Security;

/// <summary>
/// Thin plumbing shared by every on-device state store: resolves the file into the hardened
/// <see cref="SecureDataPaths"/> folder (SYSTEM/Admins full, Users read-only) and reads/writes
/// it through <see cref="StateProtection"/> (DPAPI-encrypted + tamper-evident). Migrates any
/// legacy plaintext JSON from AppData automatically on first read.
///
/// The point: a supervised user can no longer open a state file in Notepad to relax their own
/// restrictions — the folder is not user-writable, the bytes are encrypted, and any edit to the
/// ciphertext trips a tamper status so the caller can fail closed instead of trusting it.
/// </summary>
internal static class SecureStateFile
{
    public static StateReadStatus Read(string fileName, out string json) =>
        StateProtection.TryRead(SecureDataPaths.PathFor(fileName), out json);

    public static void Write(string fileName, string json) =>
        StateProtection.Write(SecureDataPaths.PathFor(fileName), json);

    public static void Delete(string fileName) =>
        StateProtection.Delete(SecureDataPaths.PathFor(fileName));

    public static bool Exists(string fileName) =>
        File.Exists(SecureDataPaths.PathFor(fileName));
}
