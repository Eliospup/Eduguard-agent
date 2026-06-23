namespace EduGuardAgent.Models;

internal enum TodayRuleIconType
{
    Supervision,
    ScreenTime,
    Gaming,
    Study,
    Bedtime,
    AppLimit,
    YouTube,
}

internal sealed class TodayRuleLine
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public required string IconGlyph { get; init; }
    public TodayRuleIconType IconType { get; init; }
    public bool IsEmphasis { get; init; }
    public double? Progress { get; init; }
    public bool ShowProgress => Progress is not null;
}
