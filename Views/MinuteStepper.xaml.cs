using System;
using System.Windows;
using System.Windows.Controls;

namespace EduGuardAgent.Views;

/// <summary>
/// A compact minutes editor: a typeable number with up/down steppers (by <see cref="Step"/>,
/// clamped to <see cref="Min"/>..<see cref="Max"/>) and a "min" suffix. Replaces the raw numeric
/// text boxes used for screen-time / play / YouTube limits.
/// </summary>
public partial class MinuteStepper : UserControl
{
    public static readonly DependencyProperty MinutesProperty =
        DependencyProperty.Register(
            nameof(Minutes),
            typeof(int),
            typeof(MinuteStepper),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(nameof(Step), typeof(int), typeof(MinuteStepper), new PropertyMetadata(15));

    public static readonly DependencyProperty MinProperty =
        DependencyProperty.Register(nameof(Min), typeof(int), typeof(MinuteStepper), new PropertyMetadata(0));

    public static readonly DependencyProperty MaxProperty =
        DependencyProperty.Register(nameof(Max), typeof(int), typeof(MinuteStepper), new PropertyMetadata(1440));

    public int Minutes
    {
        get => (int)GetValue(MinutesProperty);
        set => SetValue(MinutesProperty, value);
    }

    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int Min
    {
        get => (int)GetValue(MinProperty);
        set => SetValue(MinProperty, value);
    }

    public int Max
    {
        get => (int)GetValue(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    public MinuteStepper() => InitializeComponent();

    private void OnUp(object sender, RoutedEventArgs e) => Nudge(+Math.Max(1, Step));

    private void OnDown(object sender, RoutedEventArgs e) => Nudge(-Math.Max(1, Step));

    private void Nudge(int delta) => Minutes = Math.Clamp(Minutes + delta, Min, Max);
}
