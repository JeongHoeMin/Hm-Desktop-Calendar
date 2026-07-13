using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace HmDesktopCalendar;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        Console.CancelKeyPress += OnCancelKeyPress;
#endif
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
#if DEBUG
        Console.CancelKeyPress -= OnCancelKeyPress;
#endif
    }

#if DEBUG
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current is App app)
                _ = app.RequestShutdownAsync();
            else
                (Application.Current?.ApplicationLifetime as
                    IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        });
    }
#endif

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
}
