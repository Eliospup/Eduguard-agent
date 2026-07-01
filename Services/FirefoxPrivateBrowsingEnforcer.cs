using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

/// <summary>
/// Re-grants "Run in Private Windows" for Guardi's Firefox add-on directly in the profile's
/// <c>extension-preferences.json</c>.
///
/// Firefox's enterprise <c>ExtensionSettings.private_browsing</c> policy only sets the
/// *initial* value for a fresh install — it does not lock the per-add-on toggle in
/// about:addons. A supervised user can flip "Run in Private Windows" back off at any time,
/// silently restoring a private-browsing bypass (no image shield, no search blocking) with
/// no enforcement-side signal. This re-asserts the grant on the same enforcement cadence
/// already used for the hosts blocklist and policy ACLs elsewhere in this codebase.
///
/// Best-effort: an edit made while Firefox is running only takes effect after Firefox is
/// next restarted (Firefox reads this file at startup and may rewrite it on shutdown), same
/// caveat as the other Firefox policy files this project writes to directly.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FirefoxPrivateBrowsingEnforcer
{
    private const string PrivateBrowsingPermission = "internal:privateBrowsingAllowed";

    /// <summary>Grants the permission for <paramref name="addonId"/> in every Firefox profile found.</summary>
    public static bool EnsureGranted(string addonId)
    {
        var changedAny = false;

        foreach (var profileDir in FirefoxProfileDiscovery.EnumerateProfileDirs())
        {
            var path = Path.Combine(profileDir, "extension-preferences.json");
            if (!File.Exists(path))
                continue;

            try
            {
                if (TryGrant(path, addonId))
                    changedAny = true;
            }
            catch
            {
                // Best-effort — Firefox may have the file open right now; retried next tick.
            }
        }

        if (changedAny)
            AuditLog.Write($"SECURITY: Firefox private-browsing access for {addonId} had been revoked — re-granted.");

        return changedAny;
    }

    private static bool TryGrant(string path, string addonId)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();

        if (root[addonId] is not JsonObject entry)
        {
            entry = new JsonObject { ["permissions"] = new JsonArray(), ["origins"] = new JsonArray() };
            root[addonId] = entry;
        }

        var permissions = entry["permissions"] as JsonArray;
        if (permissions is null)
        {
            permissions = new JsonArray();
            entry["permissions"] = permissions;
        }

        foreach (var permission in permissions)
        {
            if (permission?.GetValue<string>() == PrivateBrowsingPermission)
                return false; // Already granted — nothing to do.
        }

        permissions.Add(PrivateBrowsingPermission);

        // Replace via a temp file so Firefox never observes a half-written JSON file.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString());
        File.Move(tmp, path, overwrite: true);
        return true;
    }
}
