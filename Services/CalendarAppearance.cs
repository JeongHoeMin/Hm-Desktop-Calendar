using System;

namespace HmDesktopCalendar.Services;

public sealed record CalendarAppearanceTokens(double HeaderFontSize,
    double WeekdayFontSize, double DayFontSize, double BadgeFontSize,
    double CountFontSize, double TimeFontSize, double TaskFontSize,
    double MoreFontSize, double CellHeaderHeight, double TaskRowHeight,
    double BackgroundOpacity);

public static class CalendarAppearance
{
    public const double MinimumOpacity = 0.5;
    public const double MaximumOpacity = 1.0;

    public static CalendarAppearanceTokens Create(CalendarFontScale fontScale,
        double backgroundOpacity)
    {
        double scale = fontScale switch
        {
            CalendarFontScale.Small => 0.9,
            CalendarFontScale.Large => 1.15,
            _ => 1.0
        };
        double opacity = Math.Clamp(backgroundOpacity, MinimumOpacity,
            MaximumOpacity);
        return new CalendarAppearanceTokens(
            16 * scale,
            12 * scale,
            14 * scale,
            9 * scale,
            9.5 * scale,
            10 * scale,
            11 * scale,
            10 * scale,
            18 * scale,
            16 * scale,
            opacity);
    }
}
