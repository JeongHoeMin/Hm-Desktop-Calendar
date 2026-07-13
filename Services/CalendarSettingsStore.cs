using System;
using System.IO;
using System.Text.Json;
using Avalonia;

namespace HmDesktopCalendar.Services;

public sealed class CalendarSettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "HmDesktopCalendar",
        "settings.json");

    public PixelRect Load(int defaultWidth, int defaultHeight)
    {
        try
        {
            if (File.Exists(_path) &&
                JsonSerializer.Deserialize<BoundsSettings>(
                    File.ReadAllText(_path)) is { } settings)
            {
                int width = settings.Width > 0 ? settings.Width : defaultWidth;
                int height = settings.Height > 0 ? settings.Height : defaultHeight;
                return new PixelRect(settings.X, settings.Y, width, height);
            }
        }
        catch { }
        return new PixelRect(100, 100, defaultWidth, defaultHeight);
    }

    public void Save(PixelRect bounds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(
            new BoundsSettings(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record BoundsSettings(int X, int Y, int Width = 0,
        int Height = 0);
}
