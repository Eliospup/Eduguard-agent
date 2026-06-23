using EduGuardAgent.Models;

namespace EduGuardAgent.Agent;

internal interface IAgentNotifier
{
    void Log(string message);
    void HeartbeatUpdated(HeartbeatRequest status, bool success);
    void CommandExecuted(string type, bool success);
    void DomMessageReceived(string text);
    void EnrollmentChanged(bool enrolled, string? detail = null);
    void SessionRevoked();
    void RestrictionsChanged();
    void ScreenTimeLimitReached();
    void DomLockRequested();
    void DomLockReleased();
    void AppClosedByGuardi(string processName, AppBlockCategory category);
    void BedtimeSettingsReceived(BedtimeSettingsPayload payload);
    void ExitPinReceived(string? pin);
    void GamingSettingsReceived(GamingSettingsPayload payload, bool replaceGameLists = false);
    void YoutubeSettingsReceived(YoutubeSettingsPayload payload);
    void StudyTimeSettingsReceived(StudyTimeSettingsPayload payload);
    void ModeSettingsReceived(ModeSettingsPayload payload, ScreenTimeSettingsPayload? screenTime, bool domOverride = false);
    void KioskSettingsReceived(KioskSettingsPayload payload);
    void PunishmentSettingsReceived(PunishmentSettingsPayload payload);
    void PunishmentResetRequested();
    void ImageShieldSettingsReceived(ImageShieldSettingsPayload payload);
}
