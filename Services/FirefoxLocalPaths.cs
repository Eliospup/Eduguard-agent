namespace EduGuardAgent.Services;

/// <summary>Stable paths and URL formats for local Firefox extension install.</summary>
internal static class FirefoxLocalPaths
{
    public static string ExtensionDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir,
        "extension",
        "firefox");

    public static string CanonicalXpiPath(string addonId) =>
        Path.Combine(ExtensionDir, addonId + ".xpi");

    /// <summary>
    /// Firefox expects file:///C:/path with literal spaces — not %20 from Uri.AbsoluteUri.
    /// </summary>
    public static string ToFileUrl(string path)
    {
        var normalized = Path.GetFullPath(path).Replace('\\', '/');
        return "file:///" + normalized;
    }

    /// <summary>Native path for the legacy Extensions.Install policy (//C:/...).</summary>
    public static string ToNativePolicyPath(string path)
    {
        var normalized = Path.GetFullPath(path).Replace('\\', '/');
        return "//" + normalized;
    }

    public static string EnsureCanonicalXpi(string addonId, string packedXpiPath)
    {
        Directory.CreateDirectory(ExtensionDir);
        var dest = CanonicalXpiPath(addonId);
        File.Copy(packedXpiPath, dest, overwrite: true);
        return dest;
    }
}
