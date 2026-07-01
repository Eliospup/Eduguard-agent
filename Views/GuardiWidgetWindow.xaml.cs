using System.ComponentModel;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EduGuardAgent.Profiles;
using EduGuardAgent.Services;
using EduGuardAgent.ViewModels;

namespace EduGuardAgent.Views;

[SupportedOSPlatform("windows")]
public partial class GuardiWidgetWindow : Window
{
    private const int WmShowWindow = 0x0018;
    private const double MarginPx = 18;
    private const double PromptWidth = 236;
    private const double PromptExtraHeight = 82;

    private readonly MainWindow _mainWindow;
    private HwndSource? _hwndSource;
    private INotifyPropertyChanged? _dataContextNotifier;
    private double _baseWidgetWidth = 96;
    private double _baseWidgetHeight = 138;

    public GuardiWidgetWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        DataContextChanged += OnDataContextChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        ApplyPresentation();
    }

    public void ApplyPresentation()
    {
        var presentation = UiPresentationState.Current;

        _baseWidgetWidth = presentation.WidgetWidth;
        _baseWidgetHeight = presentation.WidgetHeight;

        // Visibility is driven declaratively by IsSubMode/IsTrustedSubMode/IsRestrictedSubMode
        // bindings in XAML so it stays correct across mode changes without a manual re-sync here.
        switch (presentation.WidgetVisual)
        {
            case DesktopWidgetVisual.SoberShield:
                SoberShield.Width = presentation.WidgetIconWidth;
                SoberShield.Height = presentation.WidgetIconHeight;
                break;

            case DesktopWidgetVisual.LockedShield:
                LockedShield.Width = presentation.WidgetIconWidth;
                LockedShield.Height = presentation.WidgetIconHeight;
                break;

            default:
                Mascot.Width = presentation.WidgetIconWidth;
                Mascot.Height = presentation.WidgetIconHeight;
                break;
        }

        UpdatePromptLayout();
    }

    public void RepositionToBottomRight()
    {
        DesktopWidgetAnchor.AttachToDesktop(this);
        DesktopWidgetAnchor.PlaceBottomRight(this, MarginPx);
    }

    public void SendToDesktopLayer() => RepositionToBottomRight();

    private void OnWidgetClick(object sender, MouseButtonEventArgs e) =>
        _mainWindow.RestoreFromWidget();

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "WorkArea" or "PrimaryScreenWidth" or "PrimaryScreenHeight")
            RepositionToBottomRight();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_dataContextNotifier is not null)
            _dataContextNotifier.PropertyChanged -= OnViewModelPropertyChanged;

        _dataContextNotifier = e.NewValue as INotifyPropertyChanged;
        if (_dataContextNotifier is not null)
            _dataContextNotifier.PropertyChanged += OnViewModelPropertyChanged;

        UpdatePromptLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowWidgetPrompt) or nameof(MainViewModel.WidgetPromptText))
        {
            Dispatcher.BeginInvoke(UpdatePromptLayout, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void UpdatePromptLayout()
    {
        var showPrompt = DataContext is MainViewModel vm
            && vm.ShowWidgetPrompt
            && !string.IsNullOrWhiteSpace(vm.WidgetPromptText);

        Width = showPrompt ? Math.Max(_baseWidgetWidth, PromptWidth) : _baseWidgetWidth;
        Height = _baseWidgetHeight + (showPrompt ? PromptExtraHeight : 0);

        if (IsLoaded)
            RepositionToBottomRight();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        if (_dataContextNotifier is not null)
            _dataContextNotifier.PropertyChanged -= OnViewModelPropertyChanged;

        DataContextChanged -= OnDataContextChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        RepositionToBottomRight();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        UpdatePromptLayout();
        RepositionToBottomRight();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmShowWindow && wParam == IntPtr.Zero)
        {
            Dispatcher.BeginInvoke(() =>
            {
                DesktopWidgetAnchor.EnsureVisible(this);
                RepositionToBottomRight();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        return IntPtr.Zero;
    }
}
