namespace EduGuardAgent.Models;

internal sealed class AgentPreferences
{
    /// <summary>
    /// When null, auto-start defaults to on the first time supervision becomes active.
    /// </summary>
    public bool? AutoStartEnabled { get; set; }

    /// <summary>True once the Sub has completed the post-onboarding welcome tour.</summary>
    public bool WelcomeTourSeen { get; set; }
}
