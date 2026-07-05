using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace EduGuardAgent.Views;

/// <summary>
/// A day-of-week chip selector bound to a comma-separated token string (e.g. "mon,tue,wed").
/// Replaces the free-text days box so the Dom can just tap the days instead of typing tokens.
/// </summary>
public partial class DayChips : UserControl
{
    private static readonly (string Token, string Label)[] DayDefs =
    [
        ("mon", "Mon"), ("tue", "Tue"), ("wed", "Wed"), ("thu", "Thu"),
        ("fri", "Fri"), ("sat", "Sat"), ("sun", "Sun"),
    ];

    private readonly Dictionary<string, ToggleButton> _chips = new(StringComparer.OrdinalIgnoreCase);
    private bool _syncing;

    public static readonly DependencyProperty DaysProperty =
        DependencyProperty.Register(
            nameof(Days),
            typeof(string),
            typeof(DayChips),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDaysChanged));

    public string Days
    {
        get => (string)GetValue(DaysProperty);
        set => SetValue(DaysProperty, value);
    }

    public DayChips()
    {
        InitializeComponent();

        var style = (Style)Resources["DayChipToggle"];
        foreach (var (token, label) in DayDefs)
        {
            var chip = new ToggleButton
            {
                Content = label,
                Tag = token,
                Style = style,
                Margin = new Thickness(0, 0, 6, 0),
            };
            chip.Click += OnChipClick;
            _chips[token] = chip;
            ChipPanel.Children.Add(chip);
        }

        SyncFromDays();
    }

    private static void OnDaysChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DayChips)d).SyncFromDays();

    private void SyncFromDays()
    {
        if (_chips.Count == 0)
            return;

        _syncing = true;
        var set = Parse(Days);
        foreach (var (token, chip) in _chips)
            chip.IsChecked = set.Contains(token);
        _syncing = false;
    }

    private void OnChipClick(object sender, RoutedEventArgs e)
    {
        if (_syncing)
            return;

        // Rebuild in canonical Mon→Sun order from the currently-checked chips.
        Days = string.Join(",", DayDefs
            .Where(d => _chips[d.Token].IsChecked == true)
            .Select(d => d.Token));
    }

    private static HashSet<string> Parse(string? csv) =>
        new(
            (csv ?? string.Empty)
                .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
}
