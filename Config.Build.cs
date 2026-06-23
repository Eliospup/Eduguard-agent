namespace EduGuardAgent;

/// <summary>
/// Build-profile switches. Debug keeps local-test escape hatches; Release compiles them out.
/// </summary>
internal static partial class Config
{
#if DEBUG
    public const bool IsDebugBuild = true;

    public const bool TestingShortPunishment = false;
    public const bool TestingShortScreenTime = false;
    public const bool TestingShortGamingTime = false;
    public const bool TestingShortYoutubeTime = false;

    public static bool ExtensionGuardDevBypass => true;

    // Mutable in Debug so local extension docs can flip these without a Release build.
    public static bool ExtensionGuardChromiumUnpackedMode = false;
    public static bool ExtensionGuardFirefoxLocalMode = false;
#else
    public const bool IsDebugBuild = false;

    public const bool TestingShortPunishment = false;
    public const bool TestingShortScreenTime = false;
    public const bool TestingShortGamingTime = false;
    public const bool TestingShortYoutubeTime = false;

    public static bool ExtensionGuardDevBypass => false;

    public static bool ExtensionGuardChromiumUnpackedMode => false;
    public static bool ExtensionGuardFirefoxLocalMode => false;
#endif

    public static string BuildProfile => IsDebugBuild ? "Debug" : "Release";
}
