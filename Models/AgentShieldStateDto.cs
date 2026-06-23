namespace EduGuardAgent.Models;

internal sealed record AgentShieldStateDto(
    bool AgentRunning,
    bool Active,
    IReadOnlyDictionary<string, object> Managed)
{
    public static AgentShieldStateDto Inactive { get; } = new(
        true,
        false,
        new Dictionary<string, object> { ["shieldActive"] = false });
}
