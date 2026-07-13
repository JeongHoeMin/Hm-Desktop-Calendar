using System;
using Avalonia.Threading;

namespace HmDesktopCalendar.DesktopIntegration;

public enum CalendarHitTarget { None, Menu, PreviousMonth, NextMonth, Date }
public readonly record struct CalendarHit(CalendarHitTarget Target, DateOnly? Date = null);

public sealed class DesktopInteractionCoordinator : IDisposable
{
    private readonly GlobalPointerMonitor _pointer;
    private readonly DesktopIconHitTester _icons;
    private readonly DesktopSurfaceHitTester _desktopSurface;
    private readonly Func<int, int, CalendarHit> _hitTest;
    public event EventHandler? PreviousMonthRequested;
    public event EventHandler? NextMonthRequested;
    public event EventHandler? MenuRequested;
    public event EventHandler<DateOnly>? DateEditRequested;
    public DesktopInteractionCoordinator(GlobalPointerMonitor pointer,
        DesktopIconHitTester icons, DesktopSurfaceHitTester desktopSurface,
        Func<int, int, CalendarHit> hitTest)
    {
        _pointer = pointer;
        _icons = icons;
        _desktopSurface = desktopSurface;
        _hitTest = hitTest;
    }
    public void Start()
    {
        _pointer.Clicked += OnClick; _pointer.DoubleClicked += OnDoubleClick;
        try { _pointer.Start(); } catch { _pointer.Clicked -= OnClick;
            _pointer.DoubleClicked -= OnDoubleClick; throw; }
    }
    private void OnClick(object? sender, GlobalPointerMonitor.ScreenPoint p) => Dispatcher.UIThread.Post(() =>
    {
        CalendarHit hit = _hitTest(p.X, p.Y);
        if (hit.Target == CalendarHitTarget.Menu) MenuRequested?.Invoke(this, EventArgs.Empty);
        else if (hit.Target == CalendarHitTarget.PreviousMonth) PreviousMonthRequested?.Invoke(this, EventArgs.Empty);
        else if (hit.Target == CalendarHitTarget.NextMonth) NextMonthRequested?.Invoke(this, EventArgs.Empty);
    });
    private void OnDoubleClick(object? sender, GlobalPointerMonitor.ScreenPoint p) => Dispatcher.UIThread.Post(() =>
    {
        CalendarHit hit = _hitTest(p.X, p.Y);
        if (hit.Target == CalendarHitTarget.Date && hit.Date is { } date &&
            _desktopSurface.IsDesktopSurface(p.TargetWindow) &&
            !_icons.IsIconAtPoint(p.X, p.Y))
            DateEditRequested?.Invoke(this, date);
    });
    public void Dispose()
    {
        _pointer.Clicked -= OnClick; _pointer.DoubleClicked -= OnDoubleClick;
        _pointer.Dispose();
    }
}
