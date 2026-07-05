using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace EduGuardAgent.Views;

/// <summary>
/// A compact HH:mm time editor: still typeable, plus up/down steppers that nudge the time by
/// <see cref="StepMinutes"/> and wrap around midnight. Replaces the raw free-text time boxes
/// (bedtime, wake, study hours, weekly overrides) that were easy to mistype.
/// </summary>
public partial class TimeField : UserControl
{
    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register(
            nameof(Time),
            typeof(string),
            typeof(TimeField),
            new FrameworkPropertyMetadata("00:00", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty StepMinutesProperty =
        DependencyProperty.Register(nameof(StepMinutes), typeof(int), typeof(TimeField), new PropertyMetadata(5));

    public string Time
    {
        get => (string)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public int StepMinutes
    {
        get => (int)GetValue(StepMinutesProperty);
        set => SetValue(StepMinutesProperty, value);
    }

    public TimeField() => InitializeComponent();

    private void OnUp(object sender, RoutedEventArgs e) => Nudge(+StepMinutes);

    private void OnDown(object sender, RoutedEventArgs e) => Nudge(-StepMinutes);

    private void Nudge(int deltaMinutes)
    {
        if (!TryParse(Time, out var current))
            current = new TimeOnly(0, 0);

        // Snap to the step grid, then move; wrap cleanly across midnight.
        var total = current.Hour * 60 + current.Minute + deltaMinutes;
        var step = Math.Max(1, StepMinutes);
        total = (int)(Math.Round((double)total / step) * step);
        total = ((total % 1440) + 1440) % 1440;

        Time = new TimeOnly(total / 60, total % 60).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static bool TryParse(string? value, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return TimeOnly.TryParse(trimmed, CultureInfo.InvariantCulture, out time)
            || TimeOnly.TryParseExact(trimmed, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
            || TimeOnly.TryParseExact(trimmed, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
