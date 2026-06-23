using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using EduGuardAgent.Profiles;

namespace EduGuardAgent.Models;

internal enum DashboardPage
{
    Home,
    BlockedApps,
    BlockedWebsites,
    PlayTime,
    YoutubeTime,
    KioskApps,
    LocalHub,
    LocalSection,
}

internal sealed class RestrictionItem : INotifyPropertyChanged
{
    private string _description = string.Empty;

    public required string Title { get; init; }

    public required string Description
    {
        get => _description;
        set
        {
            if (_description == value)
                return;

            _description = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        }
    }

    public required string IconGlyph { get; init; }
    public bool IsActive { get; init; } = true;
    public string? NavigationTarget { get; init; }
    public bool IsNavigable => NavigationTarget is not null;
    public string StatusLabel => IsActive ? UiCopy.RestrictionActive : UiCopy.RestrictionInactive;

    public bool IsPlaceholder { get; init; }

    internal static RestrictionItem CreatePlaceholder() => new()
    {
        Title = string.Empty,
        Description = string.Empty,
        IconGlyph = string.Empty,
        IsPlaceholder = true,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class GameUsageItem
{
    public required string DisplayName { get; init; }
    public required string UsageLabel { get; init; }
    public string LimitLabel { get; init; } = string.Empty;
    public string UsageSummary { get; init; } = string.Empty;
    public bool HasLimit { get; init; }
    public double Progress { get; init; }
}

internal sealed class KioskAppItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Args { get; init; }
    public ImageSource? IconImage { get; init; }
    public string IconGlyph { get; init; } = "📦";
    public bool HasIconImage => IconImage is not null;
}

internal sealed class LocalKioskAppItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isApproved;

    public string CatalogId { get; init; } = string.Empty;
    public required string Name { get; init; }
    public required string Path { get; init; }
    public ImageSource? IconImage { get; init; }
    public string Icon { get; init; } = "📦";
    public string? Args { get; init; }

    public bool HasIconImage => IconImage is not null;

    public bool IsInstalled =>
        !string.IsNullOrWhiteSpace(Path) && File.Exists(Path);

    public bool IsApproved
    {
        get => _isApproved;
        set
        {
            if (_isApproved == value)
                return;

            _isApproved = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsApproved)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

internal sealed class LevelStep
{
    public required string Name { get; init; }
    public required string ShortLabel { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsPast { get; init; }
}

internal sealed class InfractionItem
{
    public required string Label { get; init; }
    public required string Detail { get; init; }
    public required string Time { get; init; }
}
