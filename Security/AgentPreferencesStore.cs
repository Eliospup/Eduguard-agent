using System.Text.Json;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

internal sealed class AgentPreferencesStore
{
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Config.AgentDataDir,
        "agent_preferences.json");

    public AgentPreferences Load()
    {
        if (!File.Exists(PreferencesPath))
            return new AgentPreferences();

        try
        {
            var json = File.ReadAllText(PreferencesPath);
            return JsonSerializer.Deserialize<AgentPreferences>(json) ?? new AgentPreferences();
        }
        catch
        {
            return new AgentPreferences();
        }
    }

    public void Save(AgentPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        File.WriteAllText(
            PreferencesPath,
            JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }
}
