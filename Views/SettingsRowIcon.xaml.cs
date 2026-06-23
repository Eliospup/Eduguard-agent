using System.Windows;
using System.Windows.Controls;

namespace EduGuardAgent.Views;

public partial class SettingsRowIcon : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(SettingsRowIcon),
            new PropertyMetadata(string.Empty));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public SettingsRowIcon() => InitializeComponent();
}
