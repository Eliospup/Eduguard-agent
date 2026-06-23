using System.Collections.Concurrent;
using System.Diagnostics;
using EduGuardAgent.Services;

namespace EduGuardAgent.Profiles;

internal sealed class GameCatalogEntry
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Apps { get; init; }
}

internal readonly record struct DetectedGame(string Key, string DisplayName);

internal static class GameCatalog
{
    public static IReadOnlyList<GameCatalogEntry> Entries { get; } =
    [
        new() { Name = "Minecraft", Apps = ["minecraft.exe", "minecraftlauncher.exe", "minecraft.windows.exe"] },
        new() { Name = "Fortnite", Apps = ["fortniteclient-win64-shipping.exe"] },
        new() { Name = "Valorant", Apps = ["valorant-win64-shipping.exe", "valorant.exe"] },
        new() { Name = "League of Legends", Apps = ["league of legends.exe", "leagueclient.exe"] },
        new() { Name = "Counter-Strike 2", Apps = ["cs2.exe"] },
        new() { Name = "Counter-Strike: GO", Apps = ["csgo.exe"] },
        new() { Name = "Dota 2", Apps = ["dota2.exe"] },
        new() { Name = "Apex Legends", Apps = ["r5apex.exe"] },
        new() { Name = "Call of Duty", Apps = ["cod.exe", "modernwarfare.exe", "blackopscoldwar.exe", "cod22-cod.exe", "cod23-cod.exe"] },
        new() { Name = "Overwatch 2", Apps = ["overwatch.exe"] },
        new() { Name = "Grand Theft Auto V", Apps = ["gta5.exe", "gtavlauncher.exe", "gta5_enhanced.exe"] },
        new() { Name = "Red Dead Redemption 2", Apps = ["rdr2.exe"] },
        new() { Name = "Roblox", Apps = ["robloxplayerbeta.exe", "robloxplayer.exe"] },
        new() { Name = "Genshin Impact", Apps = ["genshinimpact.exe", "yuanshen.exe"] },
        new() { Name = "World of Warcraft", Apps = ["wow.exe", "wowclassic.exe"] },
        new() { Name = "Rocket League", Apps = ["rocketleague.exe"] },
        new() { Name = "Rainbow Six Siege", Apps = ["rainbowsix.exe", "rainbowsix_vulkan.exe"] },
        new() { Name = "PUBG", Apps = ["tslgame.exe"] },
        new() { Name = "Destiny 2", Apps = ["destiny2.exe"] },
        new() { Name = "Elden Ring", Apps = ["eldenring.exe"] },
        new() { Name = "The Witcher 3", Apps = ["witcher3.exe"] },
        new() { Name = "Cyberpunk 2077", Apps = ["cyberpunk2077.exe"] },
        new() { Name = "FIFA / EA FC", Apps = ["fifa23.exe", "fc24.exe", "fc25.exe", "fc26.exe"] },
        new() { Name = "Among Us", Apps = ["among us.exe"] },
        new() { Name = "Terraria", Apps = ["terraria.exe"] },
        new() { Name = "Stardew Valley", Apps = ["stardew valley.exe", "stardewvalley.exe"] },
        new() { Name = "Sea of Thieves", Apps = ["sot.exe", "seaofthieves.exe"] },
        new() { Name = "Halo Infinite", Apps = ["haloinfinite.exe"] },
        new() { Name = "Palworld", Apps = ["palworld-win64-shipping.exe", "palworld.exe"] },
        new() { Name = "Helldivers 2", Apps = ["helldivers2.exe"] },
        new() { Name = "Baldur's Gate 3", Apps = ["bg3.exe", "bg3_dx11.exe"] },
        new() { Name = "Hogwarts Legacy", Apps = ["hogwartslegacy.exe"] },
        new() { Name = "EA Sports / Madden", Apps = ["madden24.exe", "madden25.exe"] },
        new() { Name = "Marvel Rivals", Apps = ["marvel-win64-shipping.exe"] },
    ];

    private static readonly Dictionary<string, string> NameByExe = BuildLookup();

    private static readonly string[] GamePathMarkers =
    [
        @"\steamapps\common\",
        @"\gog galaxy\games\",
        @"\riot games\",
        @"\epic games\",
    ];

    private static readonly HashSet<string> NonGameExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "steamservice.exe",
        "epicgameslauncher.exe", "epicwebhelper.exe", "unrealcefsubprocess.exe",
        "riotclientservices.exe", "riotclientux.exe", "riotclientuxrender.exe", "riotclient.exe",
        "galaxyclient.exe", "galaxyclienthelper.exe",
        "origin.exe", "eadesktop.exe", "eabackgroundservice.exe", "easteamservice.exe",
        "ubisoftconnect.exe", "upc.exe", "uplaywebcore.exe",
        "battle.net.exe", "blizzarderror.exe", "agent.exe",
        "crashpad_handler.exe", "crashreportclient.exe",
    };

    private static readonly ConcurrentDictionary<string, bool> HeuristicCache = new(StringComparer.OrdinalIgnoreCase);

    private static GamingGameRegistry? _registry;

    internal static void Bind(GamingGameRegistry registry) => _registry = registry;

    public static bool TryResolveForegroundGame(string foregroundExe, out DetectedGame game)
    {
        game = default;
        var exe = NormalizeExe(foregroundExe);

        if (NonGameExes.Contains(exe) || IsIgnored(exe))
            return false;

        if (_registry?.TryGetExtraName(exe, out var extraName) == true)
        {
            game = new DetectedGame(exe, extraName);
            return true;
        }

        if (NameByExe.TryGetValue(exe, out var catalogName))
        {
            game = new DetectedGame(exe, catalogName);
            return true;
        }

        if (HeuristicCache.TryGetValue(exe, out var cached))
        {
            if (!cached)
                return false;

            game = new DetectedGame(exe, AppDisplayNames.Resolve(exe));
            return true;
        }

        return TryProbeHeuristicGame(exe, out game);
    }

    public static IReadOnlyList<DetectedGame> GetRunningGames()
    {
        var detected = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var exe = NormalizeExe(process.ProcessName);
                if (detected.ContainsKey(exe))
                    continue;

                if (NonGameExes.Contains(exe) || IsIgnored(exe))
                    continue;

                if (_registry?.TryGetExtraName(exe, out var extraName) == true)
                {
                    detected[exe] = new DetectedGame(exe, extraName);
                    continue;
                }

                if (NameByExe.TryGetValue(exe, out var catalogName))
                {
                    detected[exe] = new DetectedGame(exe, catalogName);
                    continue;
                }

                if (IsHeuristicGame(exe, process))
                    detected[exe] = new DetectedGame(exe, AppDisplayNames.Resolve(exe));
            }
            catch
            {
                // Ignore processes we cannot inspect.
            }
            finally
            {
                process.Dispose();
            }
        }

        return detected.Values.ToList();
    }

    public static bool IsGameProcess(string processName)
    {
        var exe = NormalizeExe(processName);
        if (IsIgnored(exe))
            return false;

        return _registry?.IsExtraGame(exe) == true
            || NameByExe.ContainsKey(exe)
            || (HeuristicCache.TryGetValue(exe, out var isGame) && isGame);
    }

    public static string ResolveName(string processName)
    {
        var exe = NormalizeExe(processName);
        if (_registry?.TryGetExtraName(exe, out var extraName) == true)
            return extraName;

        return NameByExe.TryGetValue(exe, out var name) ? name : AppDisplayNames.Resolve(exe);
    }

    private static bool IsIgnored(string exe) => _registry?.IsIgnored(exe) ?? false;

    private static bool TryProbeHeuristicGame(string exe, out DetectedGame game)
    {
        game = default;
        var processName = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? exe[..^4]
            : exe;

        var probed = false;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            probed = true;
            try
            {
                if (!IsHeuristicGame(exe, process))
                    continue;

                game = new DetectedGame(exe, AppDisplayNames.Resolve(exe));
                return true;
            }
            finally
            {
                process.Dispose();
            }
        }

        if (probed)
            HeuristicCache[exe] = false;

        return false;
    }

    private static bool IsHeuristicGame(string exe, Process process)
    {
        if (HeuristicCache.TryGetValue(exe, out var cached))
            return cached;

        var isGame = false;
        try
        {
            var path = process.MainModule?.FileName;
            if (!string.IsNullOrEmpty(path))
            {
                foreach (var marker in GamePathMarkers)
                {
                    if (path.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        isGame = true;
                        break;
                    }
                }
            }
        }
        catch
        {
            // MainModule is inaccessible for some processes (bitness/permissions).
            HeuristicCache[exe] = false;
            return false;
        }

        HeuristicCache[exe] = isGame;
        return isGame;
    }

    private static Dictionary<string, string> BuildLookup()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            foreach (var app in entry.Apps)
                map[NormalizeExe(app)] = entry.Name;
        }

        return map;
    }

    private static string NormalizeExe(string name)
    {
        var trimmed = name.Trim().ToLowerInvariant();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}.exe";
    }
}
