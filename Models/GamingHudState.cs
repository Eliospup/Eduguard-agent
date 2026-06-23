namespace EduGuardAgent.Models;

public sealed class GamingHudState
{
    public required string GameName { get; init; }
    public required string RemainingLabel { get; init; }
    public double Progress { get; init; }
}
