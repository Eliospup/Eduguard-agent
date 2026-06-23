using EduGuardAgent.Models;
using EduGuardAgent.Services;

namespace EduGuardAgent.Profiles;

internal static class ExtensionGuardCopy
{
    public const string FirefoxReleaseBlockedTitle = "Oopsy — wrong Firefox! 🦊";

    public const string FirefoxReleaseBlockedMessage =
        "That's regular Mozilla Firefox, sweetie. Guardi can only put his picture-shield on " +
        "Firefox Developer Edition — the blue one! Ask a grown-up to open that browser instead. 💙";

    public static ExtensionGuardState Restarting(IReadOnlyList<string> browsers) => new(
        ExtensionGuardPhase.Restarting,
        browsers,
        "Hold on, sweetie 🛡️",
        $"{UiCopy.MascotName} is getting {Join(browsers)} ready. You'll be back in just a moment.");

    public static ExtensionGuardState StartupFirefoxRestart(IReadOnlyList<string> browsers) => new(
        ExtensionGuardPhase.Restarting,
        browsers,
        StartupFirefoxRestartTitle,
        StartupFirefoxRestartToast);

    public const string StartupFirefoxRestartTitle = "Restarting Firefox… 🦊";

    public const string StartupFirefoxRestartToast =
        "Guardi is restarting Mozilla Firefox so supervision and the extension can start.";

    public const string StartupFirefoxStoreUpdateTitle = "Updating Firefox shield… 🦊";

    public static string StartupFirefoxStoreUpdateToast(string version) =>
        $"Guardi is restarting Mozilla Firefox to load image shield {version} from the store.";

    public const string StartupChromiumRestartTitle = "Restarting Chrome… 🛡️";

    public const string StartupChromiumRestartToast =
        "Guardi is restarting Chrome so the image shield can load.";

    public static string SoftRestartCountdownTitle(string browser) =>
        $"Restarting {browser}…";

    public static string SoftRestartCountdownMessage(string browser, int secondsRemaining) =>
        $"Guardi will restart {browser} in {secondsRemaining}s so the picture-shield can load. " +
        "Your tabs should come back.";

    public static ExtensionGuardState Installing(IReadOnlyList<string> browsers) => new(
        ExtensionGuardPhase.Installing,
        browsers,
        ChromiumUnpackedMode.IsActive && Config.ExtensionGuardEnforceChromium
            ? "Installing the shield on Chrome… 🛡️"
            : Config.ExtensionGuardFirefoxLocalMode && !Config.ExtensionGuardEnforceChromium
            ? "Installing the shield on Firefox… 🛡️"
            : "Downloading the shield from the store… 🛡️",
        ChromiumUnpackedMode.IsActive && Config.ExtensionGuardEnforceChromium
            ? $"{UiCopy.MascotName} is installing his picture-shield in {Join(browsers)}. " +
              "Restart Chrome if it was already open."
            : Config.ExtensionGuardFirefoxLocalMode && !Config.ExtensionGuardEnforceChromium
            ? $"{UiCopy.MascotName} is installing his picture-shield in {Join(browsers)}. " +
              "Restart Firefox if it was already open."
            : $"{UiCopy.MascotName} asked {Join(browsers)} to fetch his picture-shield from the official store. " +
              "This can take a few minutes the first time.");

    public static ExtensionGuardState Unsupported(IReadOnlyList<string> browsers) => new(
        ExtensionGuardPhase.Unsupported,
        browsers,
        browsers.Any(b => b.Contains("Mozilla Firefox", StringComparison.OrdinalIgnoreCase))
            ? "Use Firefox Developer Edition 🧸"
            : "This browser can't get the shield yet 🧸",
        browsers.Any(b => b.Contains("Mozilla Firefox", StringComparison.OrdinalIgnoreCase))
            ? "Mozilla Firefox cannot load Guardi's extension. Open Firefox Developer Edition instead."
            : $"{Join(browsers)} blocks unsigned extensions — Mozilla won't allow it on Release. " +
              "Either sign the extension on addons.mozilla.org (free, unlisted) " +
              "or install Firefox Developer Edition for local testing.");

    public static ExtensionGuardState StorePending(IReadOnlyList<string> browsers) => new(
        ExtensionGuardPhase.StorePending,
        browsers,
        "Shield not on the store yet 🛡️",
        $"{UiCopy.MascotName} set up Chrome, but the picture-shield isn't listed on the Chrome Web Store yet. " +
        $"{Join(browsers)} can stay open — the shield will install automatically once Google publishes it.");

    public static ExtensionGuardState ActionRequired(IReadOnlyList<string> browsers) => new(
        ExtensionGuardPhase.ActionRequired,
        browsers,
        "Guardi needs a grown-up 🧸",
        $"{Join(browsers)} still doesn't have the shield. Ask a grown-up if this stays too long.");

    public static ExtensionGuardState Outdated(IReadOnlyList<string> browsers) => new(
        ExtensionGuardPhase.Outdated,
        browsers,
        "Fresh shield coming! ✨",
        $"{UiCopy.MascotName} is refreshing his shield in {Join(browsers)}.");

    private static string Join(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return "your browser";
        if (names.Count == 1)
            return names[0];
        return string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }
}
