using System;
using System.IO;
using System.Text.Json;
using Avalonia;

namespace HmDesktopCalendar.Services;

public enum CalendarWeekStart
{
    Sunday,
    Monday
}

public enum CalendarFontScale
{
    Small,
    Medium,
    Large
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 3;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public int X { get; init; } = 100;
    public int Y { get; init; } = 100;
    public int Width { get; init; }
    public int Height { get; init; }
    public CalendarWeekStart WeekStart { get; init; } =
        CalendarWeekStart.Sunday;
    public bool ColorWeekends { get; init; } = true;
    public CalendarFontScale FontScale { get; init; } =
        CalendarFontScale.Medium;
    public double BackgroundOpacity { get; init; } = 0.9;
}

public sealed class AppSettingsChangedEventArgs(AppSettings settings) : EventArgs
{
    public AppSettings Settings { get; } = settings;
}

public sealed class CalendarSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private readonly object _gate = new();
    private readonly string _path;
    private AppSettings? _current;

    public CalendarSettingsStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "HmDesktopCalendar", "settings.json"));
    }

    public event EventHandler<AppSettingsChangedEventArgs>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate) return _current ?? new AppSettings();
        }
    }

    public AppSettings LoadSettings()
    {
        lock (_gate)
        {
            _current = ReadSettings();
            return _current;
        }
    }

    public PixelRect Load(int defaultWidth, int defaultHeight)
    {
        AppSettings settings = LoadSettings();
        int width = settings.Width > 0 ? settings.Width : defaultWidth;
        int height = settings.Height > 0 ? settings.Height : defaultHeight;
        return new PixelRect(settings.X, settings.Y, width, height);
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = Normalize(settings);
        bool changed;
        lock (_gate) changed = SaveCore(settings);
        if (changed)
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(settings));
    }

    public void Save(PixelRect bounds)
    {
        AppSettings settings;
        bool changed;
        lock (_gate)
        {
            AppSettings current = _current ??= ReadSettings();
            settings = current with
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height
            };
            changed = SaveCore(settings);
        }
        if (changed)
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(settings));
    }

    private AppSettings ReadSettings()
    {
        try
        {
            if (File.Exists(_path) &&
                JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(_path), SerializerOptions) is { } settings)
                return Normalize(settings);
        }
        catch (JsonException) { }
        catch (NotSupportedException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new AppSettings();
    }

    private void WriteSettings(AppSettings settings)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private bool SaveCore(AppSettings settings)
    {
        WriteSettings(settings);
        bool changed = _current != settings;
        _current = settings;
        return changed;
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        CalendarWeekStart weekStart = Enum.IsDefined(settings.WeekStart)
            ? settings.WeekStart : CalendarWeekStart.Sunday;
        CalendarFontScale fontScale = Enum.IsDefined(settings.FontScale)
            ? settings.FontScale : CalendarFontScale.Medium;
        double opacity = double.IsFinite(settings.BackgroundOpacity)
            ? Math.Round(Math.Clamp(settings.BackgroundOpacity,
                CalendarAppearance.MinimumOpacity,
                CalendarAppearance.MaximumOpacity), 2)
            : 0.9;
        return settings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            WeekStart = weekStart,
            FontScale = fontScale,
            BackgroundOpacity = opacity
        };
    }
}
