namespace EduGuardAgent.Models;

public sealed class YoutubeHudState
{
    public required string SourceLabel { get; init; }
    public required string RemainingLabel { get; init; }
    public double Progress { get; init; }
}
