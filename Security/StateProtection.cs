using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace EduGuardAgent.Security;

internal enum StateReadStatus
{
    /// <summary>No state file exists yet (e.g. a fresh install).</summary>
    Missing,

    /// <summary>The file decrypted cleanly; <c>content</c> is the original payload.</summary>
    Ok,

    /// <summary>The file exists but failed integrity — edited, corrupted, or foreign.</summary>
    Tampered,
}

/// <summary>
/// Encrypts security-critical state at rest so an over-the-shoulder user cannot simply
/// open the JSON in Notepad and relax their own restrictions. DPAPI also authenticates
/// the blob, so any edit to the ciphertext is detected as tampering on read, letting the
/// caller fail closed instead of trusting an attacker-supplied value.
///
/// Honest threat-model note: this uses <see cref="DataProtectionScope.LocalMachine"/>, so
/// a determined administrator who reverse-engineers the binary can still decrypt and
/// re-sign a forged blob. The goal here is defense-in-depth — it defeats the trivial
/// "edit the JSON" path and makes tampering loud (a failed integrity check that we audit
/// and fail closed on). Forgery-proof state requires the state to live under a principal
/// the supervised user does not control (a SYSTEM service).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class StateProtection
{
    // Distinguishes an encrypted file from a legacy plaintext-JSON one (which starts
    // with '{'/'['). Bump the suffix if the format ever changes.
    private const string Header = "EGP1:";

    // App-specific entropy so a blob from another DPAPI-LocalMachine app on the same
    // machine cannot be swapped in to impersonate our state.
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("EduGuard.StateProtection.v1");

    public static StateReadStatus TryRead(string path, out string content)
    {
        content = string.Empty;

        if (!File.Exists(path))
            return StateReadStatus.Missing;

        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch
        {
            return StateReadStatus.Tampered;
        }

        if (string.IsNullOrWhiteSpace(raw))
            return StateReadStatus.Missing;

        // Legacy plaintext from a pre-encryption install: accept once. The caller
        // re-saves through Write(), which migrates it to the encrypted form.
        if (!raw.StartsWith(Header, StringComparison.Ordinal))
        {
            var trimmed = raw.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                content = raw;
                return StateReadStatus.Ok;
            }

            // Neither our ciphertext nor plausible JSON — corrupted or garbage.
            return StateReadStatus.Tampered;
        }

        try
        {
            var cipher = Convert.FromBase64String(raw[Header.Length..]);
            var plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.LocalMachine);
            content = Encoding.UTF8.GetString(plain);
            return StateReadStatus.Ok;
        }
        catch
        {
            // Edited ciphertext, wrong machine, or corruption → integrity failure.
            return StateReadStatus.Tampered;
        }
    }

    public static void Write(string path, string content)
    {
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(content),
            Entropy,
            DataProtectionScope.LocalMachine);

        var payload = Header + Convert.ToBase64String(cipher);

        // Under lockdown the secure folder is SYSTEM-only, so the (merely elevated) agent
        // cannot write it directly — it asks the SYSTEM guardian to persist on its behalf.
        if (ShouldRouteToGuardian())
        {
            if (TryGuardianWrite(path, payload, out var ipcError))
                return;
            AuditLog.Write($"Secure write via guardian failed ({ipcError}); trying direct.");
        }

        try
        {
            WriteDirect(path, payload);
        }
        catch (Exception ex)
        {
            // If the guardian is down while lockdown is on, the folder is SYSTEM-only and a
            // direct write is denied. Persistence is best-effort — never crash the agent over
            // it; the in-memory value stands and the next write retries via the guardian.
            AuditLog.Write($"Secure write to {Path.GetFileName(path)} failed: {ex.Message}");
        }
    }

    public static void Delete(string path)
    {
        if (ShouldRouteToGuardian())
        {
            if (TryGuardianDelete(path, out var ipcError))
                return;
            AuditLog.Write($"Secure delete via guardian failed ({ipcError}); trying direct.");
        }

        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static bool ShouldRouteToGuardian() =>
        SecureLockdown.IsEnabled() && !SecureLockdown.IsSystem();

    // Self-heal: if the guardian's pipe isn't answering, it may be down — kick it via its
    // scheduled task and retry once before giving up.
    private static bool TryGuardianWrite(string path, string payload, out string? error)
    {
        if (SecureStateIpcClient.TryWrite(path, payload, out error))
            return true;

        // Don't block the caller (often the UI thread) on a schtasks spawn plus a second
        // pipe timeout — kick the guardian in the background so the NEXT call benefits,
        // and fall back to a direct write now instead of stacking another multi-second wait.
        Task.Run(() => SystemGuardian.StartIfNotRunning());
        return false;
    }

    private static bool TryGuardianDelete(string path, out string? error)
    {
        if (SecureStateIpcClient.TryDelete(path, out error))
            return true;

        Task.Run(() => SystemGuardian.StartIfNotRunning());
        return false;
    }

    private static void WriteDirect(string path, string payload)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Atomic replace: this file is polled cross-process every ~400ms, so write to a
        // temp file and move it into place. That guarantees a reader never sees a partial
        // write (which would otherwise look like tampering and trip a false fail-closed).
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, payload);
        File.Move(tmp, path, overwrite: true);
    }
}
