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
        // --- Shooters / battle royale ---
        new() { Name = "Minecraft", Apps = ["minecraft.exe", "minecraftlauncher.exe", "minecraft.windows.exe"] },
        new() { Name = "Fortnite", Apps = ["fortniteclient-win64-shipping.exe"] },
        new() { Name = "Valorant", Apps = ["valorant-win64-shipping.exe", "valorant.exe"] },
        new() { Name = "League of Legends", Apps = ["league of legends.exe", "leagueclient.exe"] },
        new() { Name = "Counter-Strike 2", Apps = ["cs2.exe"] },
        new() { Name = "Counter-Strike: GO", Apps = ["csgo.exe"] },
        new() { Name = "Dota 2", Apps = ["dota2.exe"] },
        new() { Name = "Apex Legends", Apps = ["r5apex.exe", "r5apex_dx12.exe"] },
        new() { Name = "Call of Duty", Apps = ["cod.exe", "modernwarfare.exe", "blackopscoldwar.exe", "cod22-cod.exe", "cod23-cod.exe", "cod24-cod.exe"] },
        new() { Name = "Overwatch 2", Apps = ["overwatch.exe"] },
        new() { Name = "Rainbow Six Siege", Apps = ["rainbowsix.exe", "rainbowsix_vulkan.exe"] },
        new() { Name = "PUBG", Apps = ["tslgame.exe"] },
        new() { Name = "Destiny 2", Apps = ["destiny2.exe"] },
        new() { Name = "The Finals", Apps = ["discovery-win64-shipping.exe"] },
        new() { Name = "XDefiant", Apps = ["xdefiant.exe"] },
        new() { Name = "Splitgate", Apps = ["portalwars-win64-shipping.exe"] },
        new() { Name = "Paladins", Apps = ["paladins.exe"] },
        new() { Name = "Smite", Apps = ["smite.exe"] },
        new() { Name = "Team Fortress 2", Apps = ["tf_win64.exe"] },
        new() { Name = "Garry's Mod", Apps = ["gmod.exe"] },
        new() { Name = "Escape from Tarkov", Apps = ["escapefromtarkov.exe"] },
        new() { Name = "Warframe", Apps = ["warframe.x64.exe"] },
        new() { Name = "Marvel Rivals", Apps = ["marvel-win64-shipping.exe"] },
        new() { Name = "Deep Rock Galactic", Apps = ["fsd-win64-shipping.exe"] },
        new() { Name = "Warhammer 40K: Darktide", Apps = ["darktide.exe"] },
        new() { Name = "Vermintide 2", Apps = ["vermintide2.exe"] },
        new() { Name = "Helldivers 2", Apps = ["helldivers2.exe"] },

        // --- Sandbox / survival / co-op ---
        new() { Name = "Roblox", Apps = ["robloxplayerbeta.exe", "robloxplayer.exe"] },
        new() { Name = "Rust", Apps = ["rustclient.exe"] },
        new() { Name = "ARK: Survival", Apps = ["shootergame.exe", "arkascended.exe"] },
        new() { Name = "DayZ", Apps = ["dayz_x64.exe"] },
        new() { Name = "Valheim", Apps = ["valheim.exe"] },
        new() { Name = "Palworld", Apps = ["palworld-win64-shipping.exe", "palworld.exe"] },
        new() { Name = "Enshrouded", Apps = ["enshrouded.exe"] },
        new() { Name = "Sons of the Forest", Apps = ["sonsoftheforest.exe"] },
        new() { Name = "The Forest", Apps = ["theforest.exe"] },
        new() { Name = "Raft", Apps = ["raft.exe"] },
        new() { Name = "Subnautica", Apps = ["subnautica.exe"] },
        new() { Name = "No Man's Sky", Apps = ["nms.exe"] },
        new() { Name = "7 Days to Die", Apps = ["7daystodie.exe"] },
        new() { Name = "Terraria", Apps = ["terraria.exe"] },
        new() { Name = "Stardew Valley", Apps = ["stardew valley.exe", "stardewvalley.exe"] },
        new() { Name = "Don't Starve Together", Apps = ["dontstarve_steam_x64.exe"] },
        new() { Name = "Sea of Thieves", Apps = ["sot.exe", "seaofthieves.exe"] },
        new() { Name = "Grounded", Apps = ["maine-win64-shipping.exe"] },
        new() { Name = "Satisfactory", Apps = ["factorygame-win64-shipping.exe"] },
        new() { Name = "Once Human", Apps = ["oncehuman.exe"] },

        // --- Party / horror co-op ---
        new() { Name = "Among Us", Apps = ["among us.exe"] },
        new() { Name = "Fall Guys", Apps = ["fallguys_client_game.exe", "fallguys_client.exe"] },
        new() { Name = "Dead by Daylight", Apps = ["deadbydaylight-win64-shipping.exe"] },
        new() { Name = "Phasmophobia", Apps = ["phasmophobia.exe"] },
        new() { Name = "Lethal Company", Apps = ["lethal company.exe"] },
        new() { Name = "Content Warning", Apps = ["content warning.exe"] },
        new() { Name = "Brawlhalla", Apps = ["brawlhalla.exe"] },

        // --- Open world / action-adventure ---
        new() { Name = "Grand Theft Auto V", Apps = ["gta5.exe", "gtavlauncher.exe", "gta5_enhanced.exe"] },
        new() { Name = "Red Dead Redemption 2", Apps = ["rdr2.exe"] },
        new() { Name = "Cyberpunk 2077", Apps = ["cyberpunk2077.exe"] },
        new() { Name = "The Witcher 3", Apps = ["witcher3.exe"] },
        new() { Name = "Elden Ring", Apps = ["eldenring.exe"] },
        new() { Name = "Hogwarts Legacy", Apps = ["hogwartslegacy.exe"] },
        new() { Name = "Starfield", Apps = ["starfield.exe"] },
        new() { Name = "Fallout 4", Apps = ["fallout4.exe"] },
        new() { Name = "Fallout 76", Apps = ["fallout76.exe"] },
        new() { Name = "Skyrim", Apps = ["skyrimse.exe", "tesv.exe"] },
        new() { Name = "Baldur's Gate 3", Apps = ["bg3.exe", "bg3_dx11.exe"] },
        new() { Name = "Divinity: Original Sin 2", Apps = ["eocapp.exe"] },
        new() { Name = "God of War", Apps = ["gow.exe"] },
        new() { Name = "Marvel's Spider-Man", Apps = ["spider-man.exe", "miles morales.exe"] },
        new() { Name = "Horizon Zero Dawn", Apps = ["horizonzerodawn.exe"] },
        new() { Name = "Assassin's Creed", Apps = ["acmirage.exe", "acvalhalla.exe", "acorigins.exe", "acodyssey.exe"] },
        new() { Name = "Far Cry", Apps = ["farcry6.exe", "farcry5.exe"] },
        new() { Name = "Sekiro", Apps = ["sekiro.exe"] },
        new() { Name = "Dark Souls III", Apps = ["darksoulsiii.exe"] },
        new() { Name = "Star Wars Jedi", Apps = ["swgame-win64-shipping.exe"] },
        new() { Name = "Elder Scrolls Online", Apps = ["eso64.exe", "eso.exe"] },
        new() { Name = "Final Fantasy XIV", Apps = ["ffxiv_dx11.exe"] },
        new() { Name = "Monster Hunter World", Apps = ["monsterhunterworld.exe"] },
        new() { Name = "Monster Hunter Rise", Apps = ["monsterhunterrise.exe"] },

        // --- MMO / online RPG ---
        new() { Name = "World of Warcraft", Apps = ["wow.exe", "wowclassic.exe"] },
        new() { Name = "Diablo IV", Apps = ["diablo iv.exe", "fenris.exe"] },
        new() { Name = "Diablo III", Apps = ["diablo iii64.exe"] },
        new() { Name = "Hearthstone", Apps = ["hearthstone.exe"] },
        new() { Name = "StarCraft II", Apps = ["sc2.exe"] },
        new() { Name = "Lost Ark", Apps = ["lostark.exe"] },
        new() { Name = "New World", Apps = ["newworld.exe"] },
        new() { Name = "Guild Wars 2", Apps = ["gw2-64.exe"] },
        new() { Name = "Path of Exile", Apps = ["pathofexile.exe", "pathofexile_x64.exe", "pathofexilesteam.exe"] },
        new() { Name = "Genshin Impact", Apps = ["genshinimpact.exe", "yuanshen.exe"] },
        new() { Name = "Honkai: Star Rail", Apps = ["starrail.exe"] },
        new() { Name = "Wuthering Waves", Apps = ["client-win64-shipping.exe"] },
        new() { Name = "Star Citizen", Apps = ["starcitizen.exe"] },

        // --- Fighting / sports / racing ---
        new() { Name = "FIFA / EA FC", Apps = ["fifa23.exe", "fc24.exe", "fc25.exe", "fc26.exe"] },
        new() { Name = "EA Sports / Madden", Apps = ["madden24.exe", "madden25.exe", "madden26.exe"] },
        new() { Name = "Rocket League", Apps = ["rocketleague.exe"] },
        new() { Name = "Street Fighter 6", Apps = ["streetfighter6.exe"] },
        new() { Name = "Tekken 8", Apps = ["polaris-win64-shipping.exe", "tekken8.exe"] },
        new() { Name = "Mortal Kombat 1", Apps = ["mk12.exe"] },
        new() { Name = "F1", Apps = ["f1_24.exe", "f1_23.exe", "f1_25.exe"] },
        new() { Name = "Forza Horizon 5", Apps = ["forzahorizon5.exe"] },
        new() { Name = "Forza Motorsport", Apps = ["forzamotorsport.exe"] },
        new() { Name = "Euro Truck Simulator 2", Apps = ["eurotrucks2.exe"] },
        new() { Name = "American Truck Simulator", Apps = ["amtrucks.exe"] },
        new() { Name = "Farming Simulator 22", Apps = ["farmingsimulator2022.exe"] },
        new() { Name = "War Thunder", Apps = ["aces.exe"] },
        new() { Name = "World of Tanks", Apps = ["worldoftanks.exe", "wot.exe"] },

        // --- Strategy / sim ---
        new() { Name = "Civilization VI", Apps = ["civilizationvi.exe", "civilizationvi_dx12.exe"] },
        new() { Name = "Cities: Skylines", Apps = ["cities.exe", "cities2.exe"] },
        new() { Name = "Age of Empires IV", Apps = ["reliccardinal.exe"] },
        new() { Name = "Age of Empires II DE", Apps = ["aoe2de_s.exe"] },
        new() { Name = "Total War", Apps = ["warhammer3.exe", "warhammer2.exe", "three_kingdoms.exe", "pharaoh.exe"] },
        new() { Name = "Stellaris", Apps = ["stellaris.exe"] },
        new() { Name = "Crusader Kings III", Apps = ["ck3.exe"] },
        new() { Name = "Hearts of Iron IV", Apps = ["hoi4.exe"] },
        new() { Name = "Europa Universalis IV", Apps = ["eu4.exe"] },
        new() { Name = "RimWorld", Apps = ["rimworldwin64.exe"] },
        new() { Name = "Factorio", Apps = ["factorio.exe"] },
        new() { Name = "Frostpunk", Apps = ["frostpunk.exe", "frostpunk2.exe"] },
        new() { Name = "The Sims 4", Apps = ["ts4_x64.exe"] },
        new() { Name = "Football Manager", Apps = ["fm.exe", "fm24.exe"] },

        // --- Indies / roguelikes / platformers ---
        new() { Name = "Hades", Apps = ["hades.exe", "hades2.exe"] },
        new() { Name = "Hollow Knight", Apps = ["hollow_knight.exe"] },
        new() { Name = "Cuphead", Apps = ["cuphead.exe"] },
        new() { Name = "Celeste", Apps = ["celeste.exe"] },
        new() { Name = "Vampire Survivors", Apps = ["vampiresurvivors.exe"] },
        new() { Name = "Balatro", Apps = ["balatro.exe"] },
        new() { Name = "Slay the Spire", Apps = ["slaythespire.exe"] },
        new() { Name = "Dead Cells", Apps = ["deadcells.exe"] },
        new() { Name = "Enter the Gungeon", Apps = ["enterthegungeon.exe"] },
        new() { Name = "Stray", Apps = ["hk_project-win64-shipping.exe"] },
        new() { Name = "It Takes Two", Apps = ["itakestwo.exe"] },

        // --- Older mainstays still very common ---
        new() { Name = "Halo Infinite", Apps = ["haloinfinite.exe"] },
        new() { Name = "Resident Evil 4", Apps = ["re4.exe"] },
        new() { Name = "Devil May Cry 5", Apps = ["devilmaycry5.exe"] },
        new() { Name = "VRChat", Apps = ["vrchat.exe"] },
        new() { Name = "Beat Saber", Apps = ["beat saber.exe"] },
    ];

    private static readonly Dictionary<string, string> NameByExe = BuildLookup();

    // Games discovered from launcher manifests/registry at runtime (Epic, GOG, Xbox). Swapped in
    // wholesale by ApplyIndex; treated exactly like the static catalog for detection + naming.
    private static volatile IReadOnlyDictionary<string, string> _discoveredByExe =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Steam install-folder → title, from appmanifest scan. Lets a game detected purely by its
    // \steamapps\common\<dir>\ path show its real name instead of a prettified exe.
    private static volatile IReadOnlyDictionary<string, string> _steamDirToName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] GamePathMarkers =
    [
        @"\steamapps\common\",
        @"\gog galaxy\games\",
        @"\riot games\",
        @"\epic games\",
        @"\ea games\",
        @"\ea\ea games\",
        @"\origin games\",
        @"\ubisoft\ubisoft game launcher\games\",
        @"\rockstar games\",
        @"\amazon games\library\",
        @"\xboxgames\",
        @"\roblox\versions\",
    ];

    // Executables that live in game folders / launcher trees but are NOT the game: storefronts,
    // helpers, overlays, prerequisite installers, anti-cheat. Never counted or killed.
    private static readonly HashSet<string> NonGameExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "steamwebhelper.exe", "steamservice.exe", "steamerrorreporter.exe",
        "steamerrorreporter64.exe", "gameoverlayui.exe", "vrserver.exe", "vrmonitor.exe",
        "epicgameslauncher.exe", "epicwebhelper.exe", "unrealcefsubprocess.exe",
        "riotclientservices.exe", "riotclientux.exe", "riotclientuxrender.exe", "riotclient.exe",
        "galaxyclient.exe", "galaxyclienthelper.exe",
        "origin.exe", "eadesktop.exe", "eabackgroundservice.exe", "easteamservice.exe", "ealink.exe",
        "ubisoftconnect.exe", "upc.exe", "uplaywebcore.exe", "ubisoftgamelauncher.exe",
        "battle.net.exe", "blizzarderror.exe", "agent.exe",
        "rockstarservice.exe", "rockstarerrorhandler.exe", "socialclubhelper.exe", "launcher.exe",
        "amazongameslauncher.exe", "wgc.exe", "bethesdanetlauncher.exe",
        "crashpad_handler.exe", "crashreportclient.exe", "crashhandler.exe",
        "easyanticheat.exe", "beservice.exe", "battleye.exe",
        "unitycrashhandler64.exe", "unitycrashhandler32.exe",
        "msedgewebview2.exe",
    };

    // Unreal Engine's packaged-build naming (e.g. FortniteClient-Win64-Shipping.exe). Almost
    // exclusively games; a strong signal that catches modern titles the catalog doesn't list.
    private static readonly string[] ShippingSuffixes =
    [
        "-win64-shipping.exe",
        "-wingdk-shipping.exe",
        "-win32-shipping.exe",
        "-winclient-shipping.exe",
    ];

    private static readonly ConcurrentDictionary<string, bool> HeuristicCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> HeuristicNameByExe = new(StringComparer.OrdinalIgnoreCase);

    private static GamingGameRegistry? _registry;

    internal static void Bind(GamingGameRegistry registry) => _registry = registry;

    /// <summary>
    /// Replaces the auto-discovered game maps (from <see cref="GameInstallScanner"/>). Clears the
    /// heuristic caches so previously-seen exes get re-evaluated with the richer naming data.
    /// </summary>
    public static void ApplyIndex(GameInstallIndex index)
    {
        _discoveredByExe = index.ExeToName;
        _steamDirToName = index.SteamDirToName;
        HeuristicNameByExe.Clear();
        HeuristicCache.Clear();
    }

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

        if (_discoveredByExe.TryGetValue(exe, out var discoveredName))
        {
            game = new DetectedGame(exe, discoveredName);
            return true;
        }

        // Unreal shipping build — recognizable from the exe name alone, no path needed.
        if (IsShippingBuildExe(exe))
        {
            game = new DetectedGame(exe, ResolveShippingName(exe));
            return true;
        }

        if (HeuristicCache.TryGetValue(exe, out var cached))
        {
            if (!cached)
                return false;

            game = new DetectedGame(exe, ResolveHeuristicName(exe));
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

                if (_discoveredByExe.TryGetValue(exe, out var discoveredName))
                {
                    detected[exe] = new DetectedGame(exe, discoveredName);
                    continue;
                }

                if (IsShippingBuildExe(exe))
                {
                    detected[exe] = new DetectedGame(exe, ResolveShippingName(exe));
                    continue;
                }

                if (IsHeuristicGame(exe, process))
                    detected[exe] = new DetectedGame(exe, ResolveHeuristicName(exe));
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
        if (IsIgnored(exe) || NonGameExes.Contains(exe))
            return false;

        return _registry?.IsExtraGame(exe) == true
            || NameByExe.ContainsKey(exe)
            || _discoveredByExe.ContainsKey(exe)
            || IsShippingBuildExe(exe)
            || (HeuristicCache.TryGetValue(exe, out var isGame) && isGame);
    }

    public static string ResolveName(string processName)
    {
        var exe = NormalizeExe(processName);
        if (_registry?.TryGetExtraName(exe, out var extraName) == true)
            return extraName;

        if (NameByExe.TryGetValue(exe, out var name))
            return name;

        if (_discoveredByExe.TryGetValue(exe, out var discovered))
            return discovered;

        if (IsShippingBuildExe(exe))
            return ResolveShippingName(exe);

        return ResolveHeuristicName(exe);
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

                game = new DetectedGame(exe, ResolveHeuristicName(exe));
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
                var normalizedPath = path.Replace('/', '\\');
                foreach (var marker in GamePathMarkers)
                {
                    if (normalizedPath.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        isGame = true;
                        CacheHeuristicName(exe, normalizedPath);
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

    /// <summary>
    /// Derives a friendly name for a path-detected game. Steam games resolve to their real title
    /// via the appmanifest scan (install folder → name); everything else falls back to a
    /// prettified exe name.
    /// </summary>
    private static void CacheHeuristicName(string exe, string normalizedPath)
    {
        var steamName = TryResolveSteamName(normalizedPath);
        HeuristicNameByExe[exe] = steamName ?? AppDisplayNames.Resolve(exe);
    }

    private static string? TryResolveSteamName(string normalizedPath)
    {
        const string marker = @"\steamapps\common\";
        var idx = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        var rest = normalizedPath[(idx + marker.Length)..];
        var slash = rest.IndexOf('\\');
        var dir = (slash >= 0 ? rest[..slash] : rest).Trim().ToLowerInvariant();
        if (dir.Length == 0)
            return null;

        return _steamDirToName.TryGetValue(dir, out var name) ? name : null;
    }

    private static string ResolveHeuristicName(string exe) =>
        HeuristicNameByExe.TryGetValue(exe, out var name) ? name : AppDisplayNames.Resolve(exe);

    private static bool IsShippingBuildExe(string exe) =>
        ShippingSuffixes.Any(suffix => exe.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Turns "SomethingClient-Win64-Shipping.exe" into a readable "Something".</summary>
    private static string ResolveShippingName(string exe)
    {
        var baseName = exe;
        foreach (var suffix in ShippingSuffixes)
        {
            if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^suffix.Length];
                break;
            }
        }

        // Drop trailing "Client"/"Game" that Unreal projects tack on.
        foreach (var tail in new[] { "client", "game" })
        {
            if (baseName.EndsWith(tail, StringComparison.OrdinalIgnoreCase) && baseName.Length > tail.Length)
            {
                baseName = baseName[..^tail.Length];
                break;
            }
        }

        return AppDisplayNames.Resolve(baseName + ".exe");
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
