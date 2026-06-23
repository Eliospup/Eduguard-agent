namespace EduGuardAgent.Models;

internal sealed class AgentPreferences
{
    /// <summary>
    /// When null, auto-start defaults to on the first time supervision becomes active.
    /// </summary>
    public bool? AutoStartEnabled { get; set; }
}
