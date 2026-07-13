using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HmDesktopCalendar.DesktopIntegration;

[Flags]
public enum WindowPositionFlags : uint
{
    NoSize = 0x0001,
    NoMove = 0x0002,
    NoZOrder = 0x0004,
    NoActivate = 0x0010,
    FrameChanged = 0x0020,
    ShowWindow = 0x0040,
    NoOwnerZOrder = 0x0200
}

public static class NativeWindowHandles
{
    public static readonly IntPtr Top = IntPtr.Zero;
    public static readonly IntPtr Topmost = new(-1);
}

public readonly record struct NativeWindowRect(int Left, int Top,
    int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public readonly record struct NativeMonitorWorkArea(int Left, int Top,
    int Right, int Bottom);

public interface IWindowNativeApi
{
    IntPtr FindTopLevelWindow(string className);
    IntPtr FindChildWindow(IntPtr parent, string className,
        string? windowName = null);
    IReadOnlyList<IntPtr> EnumerateTopLevelWindows();
    void RequestDesktopWorkerWindow(IntPtr progman);
    IntPtr WindowFromPoint(int screenX, int screenY);
    bool IsWindow(IntPtr window);
    IntPtr GetParent(IntPtr window);
    IntPtr GetNextWindow(IntPtr window);
    bool TrySetParent(IntPtr child, IntPtr parent, out int error);
    long GetStyle(IntPtr window);
    long GetExtendedStyle(IntPtr window);
    void SetStyle(IntPtr window, long style);
    void SetExtendedStyle(IntPtr window, long style);
    bool SetWindowPosition(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, WindowPositionFlags flags, out int error);
    bool TryGetWindowRect(IntPtr window, out NativeWindowRect bounds);
    bool TryScreenToClient(IntPtr window, int screenX, int screenY,
        out int clientX, out int clientY);
    bool ShowWindow(IntPtr window);
    IntPtr MonitorFromRect(NativeWindowRect bounds, bool nearest);
    bool TryGetMonitorWorkArea(IntPtr monitor,
        out NativeMonitorWorkArea workArea);
}

public sealed class Win32WindowNativeApi : IWindowNativeApi
{
    private const uint ProgmanSpawnWorkerMessage = 0x052C;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint GwHwndNext = 2;
    private const int SwShow = 5;
    private const uint MonitorDefaultToNull = 0;
    private const uint MonitorDefaultToNearest = 2;

    public IntPtr FindTopLevelWindow(string className) =>
        FindWindow(className, null);

    public IntPtr FindChildWindow(IntPtr parent, string className,
        string? windowName = null) =>
        FindWindowEx(parent, IntPtr.Zero, className, windowName);

    public IReadOnlyList<IntPtr> EnumerateTopLevelWindows()
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, _) => { windows.Add(window); return true; },
            IntPtr.Zero);
        return windows;
    }

    public void RequestDesktopWorkerWindow(IntPtr progman) =>
        SendMessageTimeout(progman, ProgmanSpawnWorkerMessage, IntPtr.Zero,
            IntPtr.Zero, 0, 1000, out _);

    public IntPtr WindowFromPoint(int screenX, int screenY) =>
        WindowFromPointNative(new NativePoint(screenX, screenY));

    public bool IsWindow(IntPtr window) => IsWindowNative(window);
    public IntPtr GetParent(IntPtr window) => GetParentNative(window);
    public IntPtr GetNextWindow(IntPtr window) => GetWindow(window, GwHwndNext);

    public bool TrySetParent(IntPtr child, IntPtr parent, out int error)
    {
        SetLastError(0);
        IntPtr previous = SetParent(child, parent);
        error = Marshal.GetLastWin32Error();
        return previous != IntPtr.Zero || error == 0;
    }

    public long GetStyle(IntPtr window) =>
        GetWindowLongPtr(window, GwlStyle).ToInt64();
    public long GetExtendedStyle(IntPtr window) =>
        GetWindowLongPtr(window, GwlExStyle).ToInt64();
    public void SetStyle(IntPtr window, long style) =>
        SetWindowLongPtr(window, GwlStyle, new IntPtr(style));
    public void SetExtendedStyle(IntPtr window, long style) =>
        SetWindowLongPtr(window, GwlExStyle, new IntPtr(style));

    public bool SetWindowPosition(IntPtr window, IntPtr insertAfter, int x,
        int y, int width, int height, WindowPositionFlags flags,
        out int error)
    {
        SetLastError(0);
        bool result = SetWindowPos(window, insertAfter, x, y, width, height,
            (uint)flags);
        error = result ? 0 : Marshal.GetLastWin32Error();
        return result;
    }

    public bool TryGetWindowRect(IntPtr window,
        out NativeWindowRect bounds) => GetWindowRect(window, out bounds);

    public bool TryScreenToClient(IntPtr window, int screenX, int screenY,
        out int clientX, out int clientY)
    {
        var point = new NativePoint(screenX, screenY);
        bool result = ScreenToClient(window, ref point);
        clientX = point.X;
        clientY = point.Y;
        return result;
    }

    public bool ShowWindow(IntPtr window) => ShowWindowNative(window, SwShow);

    public IntPtr MonitorFromRect(NativeWindowRect bounds, bool nearest)
    {
        NativeWindowRect copy = bounds;
        return MonitorFromRectNative(ref copy,
            nearest ? MonitorDefaultToNearest : MonitorDefaultToNull);
    }

    public bool TryGetMonitorWorkArea(IntPtr monitor,
        out NativeMonitorWorkArea workArea)
    {
        var info = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        bool result = GetMonitorInfo(monitor, ref info);
        workArea = result
            ? new NativeMonitorWorkArea(info.WorkArea.Left,
                info.WorkArea.Top, info.WorkArea.Right, info.WorkArea.Bottom)
            : default;
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y) { X = x; Y = y; }
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeWindowRect Monitor;
        public NativeWindowRect WorkArea;
        public uint Flags;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")] private static extern IntPtr WindowFromPointNative(NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeWindowRect bounds);
    [DllImport("user32.dll", EntryPoint = "IsWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowNative(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetParent")] private static extern IntPtr GetParentNative(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "MonitorFromRect")] private static extern IntPtr MonitorFromRectNative(ref NativeWindowRect bounds, uint flags);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)] private static extern int GetWindowLong32(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)] private static extern int SetWindowLong32(IntPtr window, int index, int value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern void SetLastError(int errorCode);
    [DllImport("user32.dll", EntryPoint = "ShowWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindowNative(IntPtr window, int command);

    private static IntPtr GetWindowLongPtr(IntPtr window, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(window, index) :
            new IntPtr(GetWindowLong32(window, index));
    private static IntPtr SetWindowLongPtr(IntPtr window, int index,
        IntPtr value) => IntPtr.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new IntPtr(SetWindowLong32(window, index, value.ToInt32()));
}
