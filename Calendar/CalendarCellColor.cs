using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HmDesktopCalendar.Calendar;

public readonly record struct CellColorValidation(bool IsValid,
    string NormalizedColor, string Message);

public static class CalendarCellColor
{
    public const string LightForeground = "#141A24";
    public const string DarkForeground = "#FFFFFF";

    public static CellColorValidation Validate(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (!TryParse(candidate, out int red, out int green, out int blue))
            return new CellColorValidation(false, candidate,
                "배경색은 #RRGGBB 형식으로 입력하세요.");
        return new CellColorValidation(true, $"#{red:X2}{green:X2}{blue:X2}",
            string.Empty);
    }

    public static string GetForeground(string background)
    {
        CellColorValidation validation = Validate(background);
        if (!validation.IsValid)
            throw new ArgumentException(validation.Message, nameof(background));
        TryParse(validation.NormalizedColor, out int red, out int green,
            out int blue);
        double luminance = Luminance(red, green, blue);
        double lightContrast = 1.05 / (luminance + 0.05);
        double darkLuminance = Luminance(20, 26, 36);
        double darkContrast = (Math.Max(luminance, darkLuminance) + 0.05) /
            (Math.Min(luminance, darkLuminance) + 0.05);
        return lightContrast >= darkContrast ? DarkForeground : LightForeground;
    }

    public static Guid GetDecorationId(DateOnly date)
    {
        byte[] source = Encoding.UTF8.GetBytes(
            $"hm-desktop-calendar:date-cell-background:{date:yyyy-MM-dd}");
        byte[] hash = SHA256.HashData(source);
        return new Guid(hash.AsSpan(0, 16));
    }

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
