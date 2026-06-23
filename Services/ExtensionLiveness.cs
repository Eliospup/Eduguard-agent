using System.Runtime.Versioning;

namespace EduGuardAgent.Services;

/// <summary>
/// Confirms whether the shield extension background worker is alive.
/// </summary>
internal interface IExtensionLivenessProbe
{
    /// <summary>
    /// True/false when liveness is known, <c>null</c> when there is no signal yet
    /// (callers should then fall back to on-disk presence).
    /// </summary>
    bool? IsLive(BrowserKind kind);
}