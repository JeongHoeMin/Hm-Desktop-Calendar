using System;
using System.Globalization;

namespace HmDesktopCalendar.Calendar;

public readonly record struct TextColorValidation(bool IsValid,
    string NormalizedColor, double ContrastRatio, string Message);

public static class CalendarTextColor
{
    public const string DefaultColor = "#0041E6";
    public const string LegacyDefaultColor = "#3B82F6";
    private const double MinimumContrast = 4.5;
    private const string ChipBackground = "#F8F9FC";

    public static TextColorValidation Validate(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (!TryParse(candidate, out int red, out int green, out int blue))
            return new TextColorValidation(false, candidate, 0,
                "색상은 #RRGGBB 형식으로 입력하세요.");
        string normalized = $"#{red:X2}{green:X2}{blue:X2}";
        TryParse(ChipBackground, out int backgroundRed,
            out int backgroundGreen, out int backgroundBlue);
        double contrast = Contrast(red, green, blue, backgroundRed,
            backgroundGreen, backgroundBlue);
        return contrast >= MinimumContrast
            ? new TextColorValidation(true, normalized, contrast, string.Empty)
            : new TextColorValidation(false, normalized, contrast,
                $"텍스트 대비가 {contrast:0.00}:1입니다. 4.5:1 이상이어야 합니다.");
    }

    public static string NormalizeLegacyDefault(string? value) =>
        string.Equals(value, LegacyDefaultColor,
            StringComparison.OrdinalIgnoreCase) ? DefaultColor :
        value ?? DefaultColor;

    private static bool TryParse(string value, out int red, out int green,
        out int blue)
    {
        red = green = blue = 0;
        return value.Length == 7 && value[0] == '#' &&
            int.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out red) &&
            int.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out green) &&
            int.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out blue);
    }

    private static double Contrast(int red, int green, int blue,
        int backgroundRed, int backgroundGreen, int backgroundBlue)
    {
        double foreground = Luminance(red, green, blue);
        double background = Luminance(backgroundRed, backgroundGreen,
            backgroundBlue);
        return (Math.Max(foreground, background) + 0.05) /
               (Math.Min(foreground, background) + 0.05);
    }

    private static double Luminance(int red, int green, int blue) =>
        0.2126 * Linear(red) + 0.7152 * Linear(green) +
        0.0722 * Linear(blue);

    private static double Linear(int value)
    {
        double channel = value / 255.0;
        return channel <= 0.04045 ? channel / 12.92 :
            Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
