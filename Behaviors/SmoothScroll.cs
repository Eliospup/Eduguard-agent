using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace EduGuardAgent.Behaviors;

/// <summary>
/// Adds animated, pixel-smooth mouse-wheel scrolling to a <see cref="ScrollViewer"/>.
///
/// WPF's built-in wheel handling jumps the content in large discrete steps (three text
/// lines per notch) with no interpolation between them. Even when the renderer is happily
/// drawing at 60fps, the content visibly *teleports* by big chunks, which the eye reads as
/// "low framerate" / chunky scrolling. (The <c>PanningMode</c>/<c>PanningDeceleration</c>
/// settings only smooth touch and pen panning — they do nothing for the wheel.)
///
/// This behavior intercepts the wheel, accumulates a target offset, and animates
/// <see cref="ScrollViewer.VerticalOffset"/> toward it with an ease-out curve, so the
/// content glides instead of stepping. Enable it from a style:
/// <c>&lt;Setter Property="behaviors:SmoothScroll.Enabled" Value="True" /&gt;</c>.
/// </summary>
public static class SmoothScroll
{
    private const double StepPerNotch = 160.0;

    private static readonly Duration AnimationDuration =
        new(TimeSpan.FromMilliseconds(180));

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) =>
        (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    // The offset we are currently animating toward — lets rapid notches accumulate.
    private static readonly DependencyProperty AimOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AimOffset", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0.0));

    // True while an animation is in flight, so a fresh notch keeps building on the aim
    // instead of snapping back to the (lagging) current offset.
    private static readonly DependencyProperty AnimatingProperty =
        DependencyProperty.RegisterAttached(
            "Animating", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false));

    // Animatable proxy for the read-only ScrollViewer.VerticalOffset.
    private static readonly DependencyProperty AnimatedOffsetProperty =
        DependencyProperty.RegisterAttached(
            "AnimatedOffset",
            typeof(double),
            typeof(SmoothScroll),
            new PropertyMetadata(0.0, OnAnimatedOffsetChanged));

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
            return;

        if ((bool)e.NewValue)
            scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        else
            scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        // Nothing to scroll here — let the event bubble to an outer scroller if any.
        if (scrollViewer.ScrollableHeight <= 0)
            return;

        e.Handled = true;

        var animating = (bool)scrollViewer.GetValue(AnimatingProperty);
        var aim = animating
            ? (double)scrollViewer.GetValue(AimOffsetProperty)
            : scrollViewer.VerticalOffset;

        var notches = e.Delta / 120.0;
        aim -= notches * StepPerNotch;
        aim = Math.Max(0, Math.Min(aim, scrollViewer.ScrollableHeight));
        scrollViewer.SetValue(AimOffsetProperty, aim);
        scrollViewer.SetValue(AnimatingProperty, true);

        var animation = new DoubleAnimation
        {
            From = scrollViewer.VerticalOffset,
            To = aim,
            Duration = AnimationDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) => scrollViewer.SetValue(AnimatingProperty, false);

        scrollViewer.BeginAnimation(AnimatedOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
            scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
    }
}
