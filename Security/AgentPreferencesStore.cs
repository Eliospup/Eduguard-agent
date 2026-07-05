using System.Text.Json;
using EduGuardAgent.Models;

namespace EduGuardAgent.Security;

internal sealed class AgentPreferencesStore
{
    private const string FileName = "agent_preferences.json";

    public AgentPreferences Load()
    {
        var status = SecureStateFile.Read(FileName, out var json);

        if (status == StateReadStatus.Ok)
        {
            try
            {
                return JsonSerializer.Deserialize<AgentPreferences>(json) ?? new AgentPreferences();
            }
            catch
            {
                status = StateReadStatus.Tampered;
            }
        }

        if (status == StateReadStatus.Tampered)
            AuditLog.Write("SECURITY: agent preferences failed integrity check — using defaults.");

        return new AgentPreferences();
    }

    public void Save(AgentPreferences preferences)
    {
        SecureStateFile.Write(FileName, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
    }
}
