namespace EduGuardAgent.Services;

/// <summary>Global accessor so enforcement services can read Dom policy without DI wiring.</summary>
internal static class ImageShieldPolicy
{
    public static ImageShieldPolicyService? Current { get; set; }

    public static bool ShouldEnforceBrowser(BrowserKind kind) =>
        Current?.ShouldEnforceBrowser(kind) ?? false;
}
