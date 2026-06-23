using System.Windows.Threading;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.ViewModels;

internal sealed partial class MainViewModel
{
    private const int WidgetPromptMinimumFrequencyMinutes = 1;
    private const int WidgetPromptMaximumFrequencyMinutes = 180;
    private const int WidgetPromptVisibleSeconds = 10;
    private const int WidgetPromptFirstDelaySeconds = 45;

    private DispatcherTimer? _widgetPromptTimer;
    private DateTime _nextWidgetPromptAt = DateTime.MinValue;
    private DateTime _widgetPromptHideAt = DateTime.MinValue;
    private int _lastWidgetPromptIndex = -1;
    private bool _showWidgetPrompt;
    private string _widgetPromptText = string.Empty;
    private bool _localWidgetRemindersEnabled = true;
    private int _localWidgetReminderFrequencyMinutes = 15;

    public bool ShowWidgetPrompt
    {
        get => _showWidgetPrompt;
        private set => SetField(ref _showWidgetPrompt, value);
    }

    public string WidgetPromptText
    {
        get => _widgetPromptText;
        private set => SetField(ref _widgetPromptText, value);
    }

    public bool LocalWidgetRemindersEnabled
    {
        get => _localWidgetRemindersEnabled;
        set => SetField(ref _localWidgetRemindersEnabled, value);
    }

    public int LocalWidgetReminderFrequencyMinutes
    {
        get => _localWidgetReminderFrequencyMinutes;
        set => SetField(ref _localWidgetReminderFrequencyMinutes, value);
    }

    private void InitializeWidgetPrompts()
    {
        _widgetPromptTimer ??= new DispatcherTimer(
            TimeSpan.FromSeconds(5),
            DispatcherPriority.Background,
            (_, _) => OnWidgetPromptTick(),
            _dispatcher);

        _widgetPromptTimer.Start();
        SyncWidgetPromptScheduler(restartCountdown: true);
    }

    private void StopWidgetPromptTimer() =>
        _widgetPromptTimer?.Stop();

    private void OnWidgetPromptTick()
    {
        var now = DateTime.Now;

        if (!ShouldUseWidgetPrompts())
        {
            HideWidgetPrompt();
            _nextWidgetPromptAt = DateTime.MinValue;
            return;
        }

        if (ShowWidgetPrompt && now >= _widgetPromptHideAt)
            HideWidgetPrompt();

        if (!ShowWidgetPrompt && (_nextWidgetPromptAt == DateTime.MinValue || now >= _nextWidgetPromptAt))
            ShowRandomWidgetPrompt(now);
    }

    private void SyncWidgetPromptScheduler(bool restartCountdown = false)
    {
        if (!ShouldUseWidgetPrompts())
        {
            HideWidgetPrompt();
            _nextWidgetPromptAt = DateTime.MinValue;
            return;
        }

        if (restartCountdown || _nextWidgetPromptAt == DateTime.MinValue)
        {
            HideWidgetPrompt();
            _nextWidgetPromptAt = DateTime.Now.AddSeconds(WidgetPromptFirstDelaySeconds);
        }
    }

    private bool ShouldUseWidgetPrompts() =>
        SupervisionIsActive
        && !IsKioskActive
        && UiPresentationState.Current.ShowDesktopWidget
        && _localSettings.Catalog.DesktopWidgetRemindersEnabled
        && _localSettings.Catalog.DesktopWidgetReminderFrequencyMinutes >= WidgetPromptMinimumFrequencyMinutes;

    private void ShowRandomWidgetPrompt(DateTime now)
    {
        var prompts = BuildWidgetPromptPool();
        if (prompts.Count == 0)
            return;

        var index = Random.Shared.Next(prompts.Count);
        if (prompts.Count > 1 && index == _lastWidgetPromptIndex)
            index = (index + 1) % prompts.Count;

        _lastWidgetPromptIndex = index;
        WidgetPromptText = prompts[index];
        ShowWidgetPrompt = true;
        _widgetPromptHideAt = now.AddSeconds(WidgetPromptVisibleSeconds);

        var frequency = Math.Clamp(
            _localSettings.Catalog.DesktopWidgetReminderFrequencyMinutes,
            WidgetPromptMinimumFrequencyMinutes,
            WidgetPromptMaximumFrequencyMinutes);
        _nextWidgetPromptAt = now.AddMinutes(frequency);
    }

    private void HideWidgetPrompt()
    {
        ShowWidgetPrompt = false;
        WidgetPromptText = string.Empty;
        _widgetPromptHideAt = DateTime.MinValue;
    }

    private List<string> BuildWidgetPromptPool()
    {
        var name = HasSubDisplayName ? PersonalizedName : "sweetie";
        var prompts = new List<string>
        {
            $"{name}, stay focused and keep your attention on the useful thing in front of you.",
            "Study first, play later. The fun feels better when the work is done.",
            "Gentle reminder: be careful, stay on approved things, and keep your rhythm steady.",
            "One task at a time. A calm, focused pace is enough.",
            "Before opening a distraction, pause and check whether it really helps right now.",
            "Small steps count. Keep going, keep it tidy, and do your best."
        };

        if (_studyTime.IsActiveNow)
        {
            prompts.Add("Study time is active. Stay with your work and let the distractions wait.");
            prompts.Add("This is a good moment to read, write, revise, or finish one clean task.");
        }

        if (_gaming.LimitMinutes > 0)
            prompts.Add("Don’t burn all your play time too early. Save some fun for later.");

        return prompts;
    }
}
