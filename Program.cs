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

        // Full reversible teardown — for an uninstaller / manual cleanup (run elevated).
        if (HasArg(args, "--uninstall"))
        {
            SecurityTeardown.RunAll();
            return 0;
        }

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

        if (HasArg(args, SystemGuardian.GuardianArg))
        {
            ProcessSelfProtection.Protect(out _);
            SystemGuardian.Run();
            return 0;
        }

        if (args.Any(a => string.Equals(a, "--native-messaging", StringComparison.OrdinalIgnoreCase)))
            return Services.GuardiNativeMessaging.Run();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}
