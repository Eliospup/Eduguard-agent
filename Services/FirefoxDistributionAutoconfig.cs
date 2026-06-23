namespace EduGuardAgent.Services;

/// <summary>
/// Early-boot prefs for unsigned extension sideload on Firefox Developer Edition.
/// Complements policies.json — some Firefox builds apply autoconfig before the
/// first extension scan on an existing profile.
/// </summary>
internal static class FirefoxDistributionAutoconfig
{
    private const string PrefBootstrapName = "00-eduguard-autoconfig.js";
    private const string CfgFileName = "eduguard-autoconfig.cfg";

    private const string PrefBootstrap = """
        pref("general.config.obscure_value", 0);
        pref("general.config.filename", "eduguard-autoconfig.cfg");
        """;

    private const string LockedPrefs = """
        // EduGuard — allow unsigned local extension sideload on Developer Edition.
        lockPref("xpinstall.signatures.required", false);
        lockPref("extensions.autoDisableScopes", 0);
        lockPref("extensions.enabledScopes", 15);
        """;

    public static void Deploy(string installRoot)
    {
        var distributionDir = Path.Combine(installRoot, "distribution");
        var prefDir = Path.Combine(distributionDir, "defaults", "pref");
        Directory.CreateDirectory(prefDir);

        File.WriteAllText(Path.Combine(prefDir, PrefBootstrapName), PrefBootstrap);
        File.WriteAllText(Path.Combine(distributionDir, CfgFileName), LockedPrefs);
    }

    public static void Remove(string installRoot)
    {
        var distributionDir = Path.Combine(installRoot, "distribution");
        var prefPath = Path.Combine(distributionDir, "defaults", "pref", PrefBootstrapName);
        var cfgPath = Path.Combine(distributionDir, CfgFileName);

        if (File.Exists(prefPath))
            File.Delete(prefPath);

        if (File.Exists(cfgPath))
            File.Delete(cfgPath);

        TryDeleteEmptyParents(Path.GetDirectoryName(prefPath));
    }

    private static void TryDeleteEmptyParents(string? directory)
    {
        while (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                return;

            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
