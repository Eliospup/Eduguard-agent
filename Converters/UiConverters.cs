using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EduGuardAgent;

internal sealed class ProgressWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2
            || values[0] is not double used
            || values[1] is not double total
            || total <= 0)
            return 0.0;

        var parentWidth = parameter is double width ? width : 200.0;
        var ratio = Math.Clamp(used / total, 0, 1);
        return parentWidth * ratio;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class LevelStepBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.LevelStep step)
        {
            if (step.IsCurrent)
                return BrushFromResource("SegmentActiveBrush", "#0EA5E9");
            if (step.IsPast)
                return BrushFromResource("SegmentPastBrush", "#44FFFFFF");
        }

        return BrushFromResource("SegmentInactiveBrush", "#22FFFFFF");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush BrushFromResource(string key, string fallbackHex)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush)
            return brush;

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex)!);
    }
}

internal sealed class LevelStepForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.LevelStep step && step.IsCurrent)
            return BrushFromResource("SegmentActiveForegroundBrush", "#FFFFFF");

        return BrushFromResource("SegmentInactiveForegroundBrush", "#E0F2FE");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush BrushFromResource(string key, string fallbackHex)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush)
            return brush;

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex)!);
    }
}

internal sealed class MatureRestrictionIconFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return 11.0;

        return 28.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class MatureRestrictionIconWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return FontWeights.SemiBold;

        return FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Computes a fixed tile width for three restriction cards per row (WrapPanel ItemWidth).
/// </summary>
internal sealed class RestrictionTileWidthConverter : IValueConverter
{
    private const int Columns = 3;
    private const double TileMargin = 12.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double width && width > TileMargin * Columns)
            return (width - TileMargin * Columns) / Columns;

        return 300.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class IconTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Models.TodayRuleIconType iconType
            || parameter is not string expectedTypeStr
            || !Enum.TryParse<Models.TodayRuleIconType>(expectedTypeStr, out var expectedType))
            return Visibility.Collapsed;

        return iconType == expectedType ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Returns the first letter of a name, uppercased, for a circular initials badge.</summary>
internal sealed class InitialLetterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrWhiteSpace(text)
            ? "?"
            : char.ToUpperInvariant(text.Trim()[0]).ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Binds a double to a TextBox using an invariant "." decimal point, but also accepts a
/// "," on the way back in — French Windows locales default the culture's decimal separator
/// to comma, which made the "." key on the keyboard produce text the binding couldn't parse
/// (typing "0.6" silently failed to update the source). Accepting either character sidesteps
/// the locale mismatch without forcing the user to learn a different key.
/// </summary>
internal sealed class LocaleFlexibleDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = (value as string)?.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}

internal sealed class SlugEqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;

        return string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
