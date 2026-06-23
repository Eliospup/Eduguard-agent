namespace EduGuardAgent.Models;

internal enum ExtensionGuardPhase
{
    Restarting,
    Installing,
    StorePending,
    ActionRequired,
    Outdated,
    Unsupported,
}

internal sealed record ExtensionGuardState(
    ExtensionGuardPhase Phase,
    IReadOnlyList<string> Browsers,
    string Headline,
    string Body);
