using System.Runtime.Versioning;
using EduGuardAgent.Security;

namespace EduGuardAgent;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (ProcessGuardian.TryHandleSpawnRole(args))
            return 0;

        if (ProcessGuardian.IsGuardInvocation(args))
        {
            ProcessSelfProtection.Protect(out _);
            ProcessGuardian.RunGuard();
            return 0;
        }

        // Uninstall: shows the Guardi gate (blocks during self-lock, requires the exit PIN),
        // then stops the cluster + reverses the whole footprint. Exit code drives the installer:
        // 0 = done, 1 = PIN missing/cancelled, 2 = self-locked. Runs elevated.
        if (HasArg(args, "--uninstall"))
            return UninstallGate.Run();

        // Stage 3 lockdown toggle (state becomes SYSTEM-only; admin writes go via the guardian).
        if (HasArg(args, "--enable-lockdown"))
            return SecureLockdown.Enable() ? 0 : 1;

        if (HasArg(args, "--disable-lockdown"))
            return SecureLockdown.Disable() ? 0 : 1;

        // Opt-in SYSTEM guardian management (never installed automatically).
        if (HasArg(args, "--install-system-guardian"))
            return SystemGuardian.TryInstall(out _) ? 0 : 1;

        if (HasArg(args, "--uninstall-system-guardian"))
        {
            SystemGuardian.TryUninstall(out _);
            return 0;
        }

        // Manual management of the Safe Mode boot service (also auto-installed by SecurityActivation).
        if (HasArg(args, "--install-boot-service"))
            return BootGuardianService.TryInstall(out _) ? 0 : 1;

        if (HasArg(args, "--uninstall-boot-service"))
        {
            BootGuardianService.TryUninstall(out _);
            return 0;
        }

        if (HasArg(args, SystemGuardian.GuardianArg))
        {
            ProcessSelfProtection.Protect(out _);
            SystemGuardian.Run();
            return 0;
        }

        // Windows Service entry (the SCM launches the exe with --service). Runs the Safe Mode
        // boot guardian, blocking in the SCM dispatcher until the service is stopped.
        if (HasArg(args, BootGuardianService.ServiceArg))
        {
            ProcessSelfProtection.Protect(out _);
            BootGuardianService.RunAsService();
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--native-messaging", StringComparison.OrdinalIgnoreCase)))
            return Services.GuardiNativeMessaging.Run();

        // Single-instance gate for the interactive agent. Claims the supervision slot before the
        // WPF app is created: if another Guardi is already running (a manual relaunch by the sub,
        // or a watchdog resurrection racing a still-alive instance during a slow restart), this
        // process exits cleanly here — no second window, no second supervision stack, and crucially
        // no App lifecycle, so its exit can't signal the shared guardian to stand down.
        if (!ProcessGuardian.TryStartMainRole())
            return 0;

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}
