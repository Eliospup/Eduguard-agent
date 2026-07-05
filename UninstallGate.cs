using System.Runtime.Versioning;
using System.Windows;
using EduGuardAgent.Security;
using EduGuardAgent.Services;
using EduGuardAgent.Views;

namespace EduGuardAgent;

/// <summary>
/// The Guardi-styled gate for <c>--uninstall</c>. Before anything is torn down it enforces two
/// rules: (1) the machine cannot be uninstalled while the user has self-locked themselves in,
/// and (2) the exit PIN must be entered. Only when both pass does it stop the protected process
/// cluster and reverse the entire system footprint.
///
/// Runs inside a minimal <see cref="App"/> instance in "gate mode" so the WPF windows load the
/// app's styling, while supervision is never started and no main window is opened. The exit code
/// tells the uninstaller whether to proceed (0), or abort because the PIN was wrong/cancelled (1)
/// or the user is self-locked (2).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UninstallGate
{
    public const int Allowed = 0;
    public const int PinRequired = 1;
    public const int SelfLocked = 2;

    /// <summary>Set before <see cref="App"/> runs so <c>OnStartup</c> takes the gate path.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>Entry point for <c>--uninstall</c>. Returns the process exit code.</summary>
    public static int Run()
    {
        IsActive = true;
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>Runs the gate + (on success) the teardown. Called from <see cref="App.OnStartup"/>.</summary>
    public static int Evaluate()
    {
        // 1. Never allow uninstalling while the user has self-locked themselves in.
        try
        {
            var selfLock = new SelfLockService();
            selfLock.LoadFromStorage();
            if (selfLock.IsActive)
            {
                try
                {
                    new SelfLockMessageWindow(selfLock.Remaining, selfLock.ActiveUntil ?? DateTimeOffset.Now)
                    {
                        Topmost = true,
                    }.ShowDialog();
                }
                catch
                {
                    // Best-effort UI — the block stands regardless.
                }

                AuditLog.Write("Uninstall blocked: self-lock is active.");
                return SelfLocked;
            }
        }
        catch
        {
            // If self-lock can't even be evaluated, fail safe and block the uninstall.
            return SelfLocked;
        }

        // 2. Require the exit PIN whenever one is set.
        try
        {
            var pin = new ExitPinService();
            pin.LoadFromStorage();
            if (pin.IsRequired)
            {
                var ok = new ExitPinPromptWindow(pin, "uninstall")
                {
                    Topmost = true,
                }.ShowDialog() == true;

                if (!ok)
                {
                    AuditLog.Write("Uninstall cancelled: exit PIN not provided.");
                    return PinRequired;
                }
            }
        }
        catch
        {
            // Can't show/verify the PIN → never proceed unprotected.
            return PinRequired;
        }

        // 3. Gate passed — stop the self-protecting cluster and reverse everything.
        AuditLog.Write("Uninstall authorized (PIN verified) — running full teardown.");
        UninstallStopper.StopRunningCluster();
        SecurityTeardown.RunAll();
        return Allowed;
    }
}
