using System.Text.Json;
using System.Text.Json.Serialization;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

/// <summary>
/// Persists effective security flags for cross-process readers (e.g. the watchdog).
///
/// The file is encrypted and integrity-checked (<see cref="StateProtection"/>). A tampered
/// file is read as an attempt to disable the process-killer defense, so we fail closed
/// (keep blocking) and re-persist a clean copy instead of honoring the edit.
/// </summary>
internal static class SecurityRuntimeFlags
{
    private static string FlagsPath => SecureDataPaths.PathFor("security_runtime.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static volatile bool _cachedBlockProcessKillers = true;

    // This file is polled every ~400ms by two processes; only log/repair a tamper once
    // per episode (reset on the next clean read) so we don't spam the audit log.
    private static volatile bool _tamperHandled;

    public static void Persist(ModeFeatures features)
    {
        _cachedBlockProcessKillers = features.BlockProcessKillers;

        // Re-assert the protected-folder DACL in case an admin reset it since startup.
        SecureDataPaths.ReassertAcl();

        try
        {
            var payload = new StoredFlags { BlockProcessKillers = features.BlockProcessKillers };
            StateProtection.Write(FlagsPath, JsonSerializer.Serialize(payload, JsonOptions));
            _tamperHandled = false;
        }
        catch
        {
            // Best-effort.
        }
    }

    public static bool ShouldBlockProcessKillers()
    {
        try
        {
            var status = StateProtection.TryRead(FlagsPath, out var json);

            if (status == StateReadStatus.Missing)
                return _cachedBlockProcessKillers;

            if (status == StateReadStatus.Tampered)
            {
                // Someone edited the flag file — assume an attempt to switch off the
                // killer defense. Fail closed and overwrite their edit with a clean copy.
                _cachedBlockProcessKillers = true;
                HandleTamperOnce();
                return true;
            }

            var stored = JsonSerializer.Deserialize<StoredFlags>(json, JsonOptions);
            if (stored is not null)
                _cachedBlockProcessKillers = stored.BlockProcessKillers;

            _tamperHandled = false;
            return _cachedBlockProcessKillers;
        }
        catch
        {
            return _cachedBlockProcessKillers;
        }
    }

    /// <summary>Loads persisted flags from disk before the watchdog thread starts.</summary>
    public static void EnsureLoadedFromDisk() => _ = ShouldBlockProcessKillers();

    private static void HandleTamperOnce()
    {
        if (_tamperHandled)
            return;

        _tamperHandled = true;
        AuditLog.Write("SECURITY: security_runtime flags failed integrity check — forcing process-killer defense ON.");

        try
        {
            var payload = new StoredFlags { BlockProcessKillers = true };
            StateProtection.Write(FlagsPath, JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch
        {
            // Best-effort repair; the in-memory fail-closed value still holds this session.
        }
    }

    private sealed class StoredFlags
    {
        [JsonPropertyName("blockProcessKillers")]
        public bool BlockProcessKillers { get; init; }
    }
}
