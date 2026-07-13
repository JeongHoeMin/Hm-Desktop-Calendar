using System;
using Avalonia;

namespace HmDesktopCalendar.DesktopIntegration;

public readonly record struct ScreenWindowBounds(int X, int Y, int Width,
    int Height)
{
    public PixelRect ToPixelRect() => new(X, Y, Width, Height);
    public NativeWindowRect ToNativeRect() => new(X, Y, X + Width, Y + Height);
    public static ScreenWindowBounds FromPixelRect(PixelRect bounds) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    public static ScreenWindowBounds FromNativeRect(NativeWindowRect bounds) =>
        new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
}

public readonly record struct DesktopShellHandles(IntPtr Host,
    IntPtr IconView);

public readonly record struct DesktopWindowState(bool ParentAttached,
    bool BoundsMatch, bool IconsAboveCalendar,
    ScreenWindowBounds? ActualBounds)
{
    public bool IsValid => ParentAttached && BoundsMatch && IconsAboveCalendar;
}

public enum WindowTransitionFailure
{
    None,
    WindowUnavailable,
    ShellUnavailable,
    ParentChangeFailed,
    BoundsApplyFailed,
    LayerApplyFailed,
    ValidationFailed,
    InvalidMode
}

public readonly record struct WindowTransitionResult(bool Success,
    WindowTransitionFailure Failure, string Message, DesktopWindowState State)
{
    public static WindowTransitionResult Ok(DesktopWindowState state) =>
        new(true, WindowTransitionFailure.None, string.Empty, state);
    public static WindowTransitionResult Fail(WindowTransitionFailure failure,
        string message, DesktopWindowState state = default) =>
        new(false, failure, message, state);
}

public sealed class DesktopShellLocator
{
    private readonly IWindowNativeApi _native;
    public DesktopShellLocator(IWindowNativeApi native) => _native = native;

    public bool TryLocate(bool createIfMissing, out DesktopShellHandles handles)
    {
        if (TryLocateExisting(out handles)) return true;
        if (!createIfMissing) return false;
        IntPtr progman = _native.FindTopLevelWindow("Progman");
        if (progman == IntPtr.Zero) return false;
        _native.RequestDesktopWorkerWindow(progman);
        return TryLocateExisting(out handles);
    }

    private bool TryLocateExisting(out DesktopShellHandles handles)
    {
        foreach (IntPtr window in _native.EnumerateTopLevelWindows())
        {
            IntPtr iconView = _native.FindChildWindow(window,
                "SHELLDLL_DefView");
            if (iconView == IntPtr.Zero) continue;
            handles = new DesktopShellHandles(window, iconView);
            return true;
        }
        handles = default;
        return false;
    }
}

public sealed class WindowBoundsService
{
    private readonly IWindowNativeApi _native;
    public WindowBoundsService(IWindowNativeApi native) => _native = native;

    public bool TryCapture(IntPtr window, out ScreenWindowBounds bounds)
    {
        if (_native.TryGetWindowRect(window, out NativeWindowRect nativeBounds))
        {
            bounds = ScreenWindowBounds.FromNativeRect(nativeBounds);
            return true;
        }
        bounds = default;
        return false;
    }

    public bool ApplyTopLevel(IntPtr window, ScreenWindowBounds bounds,
        out int error) => _native.SetWindowPosition(window, IntPtr.Zero,
        bounds.X, bounds.Y, bounds.Width, bounds.Height,
        WindowPositionFlags.NoZOrder | WindowPositionFlags.NoActivate |
        WindowPositionFlags.ShowWindow, out error);

    public bool ApplyAttached(IntPtr window, IntPtr desktopHost,
        ScreenWindowBounds bounds, out int error)
    {
        if (!_native.TryScreenToClient(desktopHost, bounds.X, bounds.Y,
                out int clientX, out int clientY))
        {
            error = -1;
            return false;
        }
        return _native.SetWindowPosition(window, IntPtr.Zero, clientX, clientY,
            bounds.Width, bounds.Height,
            WindowPositionFlags.NoZOrder | WindowPositionFlags.NoActivate |
            WindowPositionFlags.ShowWindow, out error);
    }

    public bool Matches(IntPtr window, ScreenWindowBounds expected,
        out ScreenWindowBounds? actual)
    {
        if (!TryCapture(window, out ScreenWindowBounds captured))
        {
            actual = null;
            return false;
        }
        actual = captured;
        return captured == expected;
    }

    public ScreenWindowBounds EnsureMinimumSize(ScreenWindowBounds bounds,
        int minWidth, int minHeight) => bounds with
        {
            Width = Math.Max(minWidth, bounds.Width),
            Height = Math.Max(minHeight, bounds.Height)
        };

    public ScreenWindowBounds RecoverIfFullyOffscreen(
        ScreenWindowBounds bounds, int minWidth, int minHeight)
    {
        bounds = EnsureMinimumSize(bounds, minWidth, minHeight);
        if (_native.MonitorFromRect(bounds.ToNativeRect(), false) != IntPtr.Zero)
            return bounds;
        IntPtr monitor = _native.MonitorFromRect(bounds.ToNativeRect(), true);
        if (monitor == IntPtr.Zero ||
            !_native.TryGetMonitorWorkArea(monitor, out var workArea))
            return bounds;
        int maxX = Math.Max(workArea.Left, workArea.Right - bounds.Width);
        int maxY = Math.Max(workArea.Top, workArea.Bottom - bounds.Height);
        return bounds with
        {
            X = Math.Clamp(bounds.X, workArea.Left, maxX),
            Y = Math.Clamp(bounds.Y, workArea.Top, maxY)
        };
    }
}

public sealed class DesktopLayerService
{
    private readonly IWindowNativeApi _native;
    public DesktopLayerService(IWindowNativeApi native) => _native = native;

    public bool AreIconsAboveCalendar(IntPtr calendar,
        DesktopShellHandles shell)
    {
        for (IntPtr current = _native.GetNextWindow(shell.IconView);
             current != IntPtr.Zero;
             current = _native.GetNextWindow(current))
        {
            if (current == calendar) return true;
        }
        return false;
    }

    public bool EnsureIconsAboveCalendar(IntPtr calendar,
        DesktopShellHandles shell, out int error) =>
        _native.SetWindowPosition(calendar, shell.IconView, 0, 0, 0, 0,
            WindowPositionFlags.NoMove | WindowPositionFlags.NoSize |
            WindowPositionFlags.NoActivate |
            WindowPositionFlags.NoOwnerZOrder, out error);
}

public sealed class DesktopAttachmentService
{
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private readonly IWindowNativeApi _native;

    public DesktopAttachmentService(IWindowNativeApi native) =>
        _native = native;

    public bool IsAttached(IntPtr calendar, DesktopShellHandles shell) =>
        _native.IsWindow(calendar) && _native.GetParent(calendar) == shell.Host;

    public bool Attach(IntPtr calendar, DesktopShellHandles shell,
        out int error)
    {
        long style = _native.GetStyle(calendar);
        _native.SetStyle(calendar, (style & ~WsPopup) | WsChild);
        long extended = _native.GetExtendedStyle(calendar);
        _native.SetExtendedStyle(calendar,
            extended | WsExToolWindow | WsExNoActivate);
        if (!_native.TrySetParent(calendar, shell.Host, out error)) return false;
        ApplyFrameChange(calendar);
        return true;
    }

    public bool Detach(IntPtr calendar, out int error)
    {
        if (!_native.TrySetParent(calendar, IntPtr.Zero, out error)) return false;
        long style = _native.GetStyle(calendar);
        _native.SetStyle(calendar, (style & ~WsChild) | WsPopup);
        long extended = _native.GetExtendedStyle(calendar);
        _native.SetExtendedStyle(calendar, extended & ~WsExNoActivate);
        ApplyFrameChange(calendar);
        return true;
    }

    public bool ActivateForeground(IntPtr calendar, out int error)
    {
        _native.ShowWindow(calendar);
        // HWND_TOPMOST leaves a persistent topmost state behind.  Once the
        // window becomes an Explorer child again that state can prevent the
        // calendar from being placed below SHELLDLL_DefView.  HWND_TOP brings
        // the editor forward without changing it into a topmost window.
        return _native.SetWindowPosition(calendar, NativeWindowHandles.Top,
            0, 0, 0, 0, WindowPositionFlags.NoMove |
            WindowPositionFlags.NoSize | WindowPositionFlags.ShowWindow,
            out error);
    }

    private void ApplyFrameChange(IntPtr calendar) =>
        _native.SetWindowPosition(calendar, IntPtr.Zero, 0, 0, 0, 0,
            WindowPositionFlags.NoMove | WindowPositionFlags.NoSize |
            WindowPositionFlags.NoZOrder | WindowPositionFlags.NoActivate |
            WindowPositionFlags.FrameChanged, out _);
}
