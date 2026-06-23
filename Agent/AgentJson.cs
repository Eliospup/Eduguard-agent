using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduGuardAgent.Agent;

internal static class AgentJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
