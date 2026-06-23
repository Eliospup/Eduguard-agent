using EduGuardAgent.Agent;
using EduGuardAgent.Models;
using EduGuardAgent.Profiles;
using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class VpnBlockingService
{
    public event Action? Changed;

    public bool IsEnabled { get; private set; }

    public void Enable(SessionState session, UrlBlockingService urls, IAgentNotifier? notifier = null)
    {
        if (IsEnabled)
            return;

        session.BeginBulkUpdate();
        try
        {
            foreach (var entry in VpnBlocklist.Entries)
            {
                foreach (var app in entry.Apps)
                    session.BlockApp(app, AppBlockCategory.VpnShield);
            }
        }
        finally
        {
            session.EndBulkUpdate();
        }

        var hosts = VpnBlocklist.Entries
            .SelectMany(e => e.Hosts)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        urls.BlockMany(hosts);

        IsEnabled = true;
        Changed?.Invoke();
        notifier?.Log($"{UiCopy.MascotName} turned on the VPN shield.");
        AuditLog.Write(
            $"VPN shield enabled ({VpnBlocklist.Entries.Count} services, {VpnBlocklist.TotalApps} apps, {VpnBlocklist.TotalHosts} hosts).");
    }

    public void Disable(SessionState session, UrlBlockingService urls, bool applyUrlChanges = true)
    {
        if (!IsEnabled)
            return;

        session.BeginBulkUpdate();
        try
        {
            foreach (var entry in VpnBlocklist.Entries)
            {
                foreach (var app in entry.Apps)
                    session.UnblockApp(app);
            }
        }
        finally
        {
            session.EndBulkUpdate();
        }

        var hosts = VpnBlocklist.Entries
            .SelectMany(e => e.Hosts)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        if (applyUrlChanges)
            urls.UnblockMany(hosts);
        else
            urls.UnblockManyWithoutApply(hosts);

        IsEnabled = false;
        Changed?.Invoke();
        AuditLog.Write("VPN shield disabled.");
    }
}
