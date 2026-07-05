using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace EduGuardAgent.Views;

/// <summary>
/// A single settings row: a label (and optional description) on the left, and any control
/// (toggle, input, buttons) on the right. Replaces the repetitive hand-rolled two-column Grids
/// that made the local settings panel verbose and inconsistent.
/// Usage: <c>&lt;views:SettingRow Label="…" Description="…"&gt;&lt;CheckBox …/&gt;&lt;/views:SettingRow&gt;</c>
/// </summary>
[ContentProperty(nameof(RowContent))]
public partial class SettingRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty RowContentProperty =
        DependencyProperty.Register(nameof(RowContent), typeof(object), typeof(SettingRow), new PropertyMetadata(null));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? RowContent
    {
        get => GetValue(RowContentProperty);
        set => SetValue(RowContentProperty, value);
    }

    public SettingRow() => InitializeComponent();
}
