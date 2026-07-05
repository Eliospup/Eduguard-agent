using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using EduGuardAgent.Security;

namespace EduGuardAgent;

[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private static volatile bool _extensionRuntimeDeactivated;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Gate mode (--uninstall): resources are loaded, but we start NO supervision and open
        // NO main window. Show the self-lock / PIN gate, run the teardown, then exit with the
        // gate's result code so the uninstaller knows whether to proceed.
        if (UninstallGate.IsActive)
        {
            var code = UninstallGate.Evaluate();
            Shutdown(code);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Security.ProcessSelfProtection.Protect(out _);
        // Note: the single-instance gate + mutual-resurrection monitor is started earlier, in
        // Program.Main via ProcessGuardian.TryStartMainRole, so a duplicate launch never reaches here.
        AuditLog.Write(
            $"Guardi starting — {Config.BuildProfile} build, endpoint {Config.BaseUrl}, " +
            $"firefoxLocal={(Config.ExtensionGuardFirefoxLocalMode ? "on" : "off")}, " +
            $"chromiumUnpacked={(Config.ExtensionGuardChromiumUnpackedMode ? "on" : "off")}, " +
            $"extensionDevBypass={(Config.ExtensionGuardDevBypass ? "on" : "off")}.");

        // Guardi exit must leave browsers running. The shield extension stays
        // installed but receives shieldActive=false so it can sleep immediately.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            ReleaseKioskBestEffort();
            DeactivateExtensionRuntimeBestEffort();
        };
        SessionEnding += (_, _) =>
        {
            ReleaseKioskBestEffort();
            DeactivateExtensionRuntimeBestEffort();
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Security.ProcessGuardian.SignalIntentionalShutdown();

        try
        {
            if (MainWindow is MainWindow window && window.DataContext is ViewModels.MainViewModel vm)
                vm.EmergencyReleaseKiosk();
        }
        catch
        {
            // Best-effort.
        }

        if (MainWindow is MainWindow main && main.DataContext is ViewModels.MainViewModel viewModel)
            viewModel.Dispose();

        DeactivateExtensionRuntimeBestEffort();
        base.OnExit(e);
    }

    private static void ReleaseKioskBestEffort()
    {
        try
        {
            if (Current?.MainWindow is MainWindow window && window.DataContext is ViewModels.MainViewModel vm)
                vm.EmergencyReleaseKiosk();
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void DeactivateExtensionRuntimeBestEffort()
    {
        if (_extensionRuntimeDeactivated)
            return;
        _extensionRuntimeDeactivated = true;

        try
        {
            using var shield = new Services.ImageBlurExtensionService();
            shield.SetRuntimeActive(false);
        }
        catch
        {
            // Best-effort: a crashing process must never throw on the way out.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            AuditLog.Write($"Unhandled UI exception: {e.Exception}");
            if (MainWindow is MainWindow window && window.DataContext is ViewModels.MainViewModel vm)
                vm.EmergencyReleaseKiosk();
        }
        catch
        {
            // Best-effort — never throw while handling an exception.
        }

        var message = e.Exception.InnerException?.Message ?? e.Exception.Message;
        MessageBox.Show(
            message,
            "EduGuard error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
