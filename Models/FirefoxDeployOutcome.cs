namespace EduGuardAgent.Models;

internal sealed record FirefoxDeployOutcome(bool ExtensionFilesChanged, bool PoliciesChanged)
{
    public bool RequiresBrowserRestart => ExtensionFilesChanged || PoliciesChanged;
}
