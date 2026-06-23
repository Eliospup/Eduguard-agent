using System.Text.Json;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

internal sealed class LocalSettingsCatalogStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "local_settings_catalog.json");

    public LocalSettingsCatalog Load()
    {
        if (!File.Exists(SettingsPath))
            return LocalSettingsCatalog.CreateDefaults();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var catalog = JsonSerializer.Deserialize<LocalSettingsCatalog>(json);
            if (catalog is null)
                return LocalSettingsCatalog.CreateDefaults();

            EnsureAllModes(catalog);
            return catalog;
        }
        catch
        {
            return LocalSettingsCatalog.CreateDefaults();
        }
    }

    public void Save(LocalSettingsCatalog catalog)
    {
        EnsureAllModes(catalog);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void EnsureAllModes(LocalSettingsCatalog catalog)
    {
        foreach (var mode in Profiles.AgentModeRegistry.All)
        {
            if (!catalog.PerMode.ContainsKey(mode.Slug))
                catalog.PerMode[mode.Slug] = LocalPerModeRuleSet.FromDefinition(mode);
        }
    }
}
