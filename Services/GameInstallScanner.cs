using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace EduGuardAgent.Services;

/// <summary>
/// The result of a launcher scan: a map of game executable → friendly display name, plus a
/// Steam install-folder → name map used to give heuristic-detected Steam games a real title
/// instead of a prettified exe name.
/// </summary>
internal sealed class GameInstallIndex
{
    public IReadOnlyDictionary<string, string> ExeToName { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> SteamDirToName { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static GameInstallIndex Empty { get; } = new();

    public int Count => ExeToName.Count;
}

/// <summary>
/// Discovers installed games by reading each launcher's own manifests/registry, so the Dom
/// doesn't have to enumerate them by hand. Sources (all best-effort, each isolated):
///   • Epic Games   — Data\Manifests\*.item (JSON: DisplayName + LaunchExecutable) → exact exe.
///   • GOG Galaxy   — HKLM\...\GOG.com\Games\* (exe + gameName) → exact exe.
///   • Steam        — steamapps\appmanifest_*.acf (name + installdir); pairs the install folder
///                    with the title so the runtime path heuristic (\steamapps\common\) can show
///                    a real game name.
///   • Xbox/Game Pass — C:\XboxGames\&lt;Game&gt;\Content\*.exe → exact exe.
/// Nothing here enforces anything; it only enriches <see cref="GameCatalog"/> so more titles are
/// auto-recognized. False positives remain suppressible via the Dom's ignore list.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class GameInstallScanner
{
    // Executables that ship alongside games but are launchers, prerequisites, crash handlers or
    // anti-cheat — never the game itself. Kept out of the exe→name map to avoid mis-counting.
    private static readonly HashSet<string> ExeBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "unitycrashhandler64.exe", "unitycrashhandler32.exe",
        "ue4prereqsetup_x64.exe", "ueprereqsetup_x64.exe", "ue5prereqsetup_x64.exe",
        "vcredist_x64.exe", "vcredist_x86.exe", "dxsetup.exe", "dxwebsetup.exe", "oalinst.exe",
        "crashreportclient.exe", "crashpad_handler.exe", "crashhandler.exe",
        "easyanticheat_setup.exe", "easyanticheat.exe", "easyanticheat_eos_setup.exe",
        "battleye.exe", "beservice.exe", "install.exe", "installer.exe", "setup.exe",
        "unins000.exe", "uninstall.exe", "notification_helper.exe", "touchup.exe",
    };

    // Steam AppIDs that are applications/tools rather than games (Steam's ACF can't tell them
    // apart), so filtering by ID keeps e.g. Wallpaper Engine or Blender out of the game list.
    private static readonly HashSet<string> SteamNonGameAppIds = new(StringComparer.Ordinal)
    {
        "431960",  // Wallpaper Engine
        "365670",  // Blender
        "1070560", // Steam Linux Runtime
        "1391110", // Steam Linux Runtime - Soldier
        "1628350", // Steam Linux Runtime - Sniper
        "228980",  // Steamworks Common Redistributables
        "250820",  // SteamVR
        "323910",  // Aseprite
        "404790",  // OBS-adjacent tools appear under their own ids; kept conservative
    };

    public static GameInstallIndex Scan()
    {
        var exeToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var steamDirToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        TrySource(() => ScanEpic(exeToName));
        TrySource(() => ScanGog(exeToName));
        TrySource(() => ScanSteam(steamDirToName));
        TrySource(() => ScanXbox(exeToName));

        return new GameInstallIndex { ExeToName = exeToName, SteamDirToName = steamDirToName };
    }

    private static void TrySource(Action scan)
    {
        try { scan(); }
        catch { /* one launcher failing must not sink the whole scan */ }
    }

    // --------------------------------------------------------------------- Epic

    private static void ScanEpic(Dictionary<string, string> map)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir))
            return;

        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                var launch = root.TryGetProperty("LaunchExecutable", out var le) ? le.GetString() : null;
                var name = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null;
                if (string.IsNullOrWhiteSpace(launch) || string.IsNullOrWhiteSpace(name))
                    continue;

                // Skip Epic's own tooling (Unreal Engine editor, etc.).
                var category = root.TryGetProperty("AppCategories", out var ac) && ac.ValueKind == JsonValueKind.Array
                    ? ac.EnumerateArray().Select(e => e.GetString()).ToArray()
                    : [];
                if (category.Any(c => string.Equals(c, "engine", StringComparison.OrdinalIgnoreCase)))
                    continue;

                var exe = Path.GetFileName(launch);
                if (string.IsNullOrWhiteSpace(exe) || ExeBlocklist.Contains(exe))
                    continue;

                map[exe.ToLowerInvariant()] = name.Trim();
            }
            catch { /* skip malformed manifest */ }
        }
    }

    // ---------------------------------------------------------------------- GOG

    private static void ScanGog(Dictionary<string, string> map)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var games = baseKey.OpenSubKey(@"SOFTWARE\GOG.com\Games")
                ?? baseKey.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (games is null)
                continue;

            foreach (var sub in games.GetSubKeyNames())
            {
                using var g = games.OpenSubKey(sub);
                if (g?.GetValue("exe") is not string exePath || string.IsNullOrWhiteSpace(exePath))
                    continue;
                if (g.GetValue("gameName") is not string name || string.IsNullOrWhiteSpace(name))
                    continue;

                var exe = Path.GetFileName(exePath);
                if (string.IsNullOrWhiteSpace(exe) || ExeBlocklist.Contains(exe))
                    continue;

                map[exe.ToLowerInvariant()] = name.Trim();
            }
        }
    }

    // -------------------------------------------------------------------- Steam

    private static void ScanSteam(Dictionary<string, string> dirMap)
    {
        var steamPath = GetSteamPath();
        if (steamPath is null)
            return;

        var libraries = new List<string> { steamPath };

        var libFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libFile))
        {
            var text = File.ReadAllText(libFile);
            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(p))
                    libraries.Add(p);
            }
        }

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var appsDir = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(appsDir))
                continue;

            foreach (var acf in Directory.EnumerateFiles(appsDir, "appmanifest_*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(acf);
                    var appid = MatchValue(text, "appid");
                    var name = MatchValue(text, "name");
                    var installDir = MatchValue(text, "installdir");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
                        continue;
                    if (appid is not null && SteamNonGameAppIds.Contains(appid))
                        continue;

                    dirMap[installDir.Trim().ToLowerInvariant()] = name.Trim();
                }
                catch { /* skip malformed acf */ }
            }
        }
    }

    private static string? GetSteamPath()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hklm.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
                ?? hklm.OpenSubKey(@"SOFTWARE\Valve\Steam");
            if (key?.GetValue("InstallPath") is string path && Directory.Exists(path))
                return path;
        }

        using var hkcu = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
        if (hkcu?.GetValue("SteamPath") is string userPath && Directory.Exists(userPath))
            return userPath;

        return null;
    }

    // --------------------------------------------------------------------- Xbox

    private static void ScanXbox(Dictionary<string, string> map)
    {
        // Game Pass / MS Store games install under a per-drive "XboxGames" root, each with a
        // named folder and the payload under \Content. The folder name is the friendly title.
        foreach (var drive in DriveInfo.GetDrives())
        {
            string root;
            try
            {
                if (!drive.IsReady)
                    continue;
                root = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
            }
            catch { continue; }

            if (!Directory.Exists(root))
                continue;

            foreach (var gameDir in SafeEnumerateDirectories(root))
            {
                var title = Path.GetFileName(gameDir);
                var content = Path.Combine(gameDir, "Content");
                if (string.IsNullOrWhiteSpace(title) || !Directory.Exists(content))
                    continue;

                foreach (var exePath in SafeEnumerateFiles(content, "*.exe"))
                {
                    var exe = Path.GetFileName(exePath);
                    if (string.IsNullOrWhiteSpace(exe) || ExeBlocklist.Contains(exe))
                        continue;

                    map[exe.ToLowerInvariant()] = title.Trim();
                }
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern)
    {
        try { return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }

    private static string? MatchValue(string vdf, string key)
    {
        var m = Regex.Match(vdf, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
