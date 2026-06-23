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

        if (args.Any(a => string.Equals(a, "--native-messaging", StringComparison.OrdinalIgnoreCase)))
            return Services.GuardiNativeMessaging.Run();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
