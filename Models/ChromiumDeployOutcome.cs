namespace EduGuardAgent.Models;

internal sealed record ChromiumDeployOutcome(bool ExtensionFilesChanged, bool PoliciesChanged)
{
    public bool RequiresBrowserRestart => ExtensionFilesChanged || PoliciesChanged;
}
