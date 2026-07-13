using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace HmDesktopCalendar.DesktopIntegration;

public sealed class GlobalPointerMonitor : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmQuit = 0x0012;

    private readonly ManualResetEventSlim _started = new(false);
    private readonly HookProcedure _hookProcedure;
    private readonly IWindowNativeApi _native;
    private readonly uint _doubleClickTime = GetDoubleClickTime();
    private readonly int _doubleClickHalfWidth = Math.Max(1, GetSystemMetrics(36) / 2);
    private readonly int _doubleClickHalfHeight = Math.Max(1, GetSystemMetrics(37) / 2);
    private Thread? _thread;
    private IntPtr _hook;
    private uint _threadId;
    private Exception? _startError;
    private uint _lastClickTime;
    private NativePoint _lastClickPoint;
    private IntPtr _lastClickWindow;

    public event EventHandler<ScreenPoint>? Clicked;
    public event EventHandler<ScreenPoint>? DoubleClicked;

    public GlobalPointerMonitor() : this(new Win32WindowNativeApi()) { }

    public GlobalPointerMonitor(IWindowNativeApi native)
    {
        _native = native;
        _hookProcedure = HookCallback;
    }

    public void Start()
    {
        if (_thread is not null)
            return;

        _startError = null;
        _started.Reset();
        _thread = new Thread(MessageThreadMain)
        {
            IsBackground = true,
            Name = "HmDesktopCalendar.MouseHook"
        };
        _thread.Start();
        _started.Wait();

        if (_startError is not null)
        {
            _thread.Join();
            _thread = null;
            throw new InvalidOperationException(
                "전역 마우스 훅을 시작하지 못했습니다.", _startError);
        }
    }

    public void Stop()
    {
        uint threadId = _threadId;
        if (threadId != 0)
            PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _threadId = 0;
        _lastClickTime = 0;
        _lastClickWindow = IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
        _started.Dispose();
        GC.SuppressFinalize(this);
    }

    private void MessageThreadMain()
    {
        _threadId = GetCurrentThreadId();
        try
        {
            // Ensure that this thread owns a message queue before Start returns.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            _hook = SetWindowsHookEx(WhMouseLl, _hookProcedure,
                GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _started.Set();
            while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _startError = exception;
            _started.Set();
        }
        finally
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _threadId = 0;
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var mouse = Marshal.PtrToStructure<LowLevelMouseData>(lParam);
                uint message = unchecked((uint)wParam.ToInt64());
                if (message == WmLButtonDown)
                {
                    IntPtr target = _native.WindowFromPoint(
                        mouse.Point.X, mouse.Point.Y);
                    ProcessLeftButtonDown(new ScreenPoint(mouse.Point.X,
                        mouse.Point.Y, target), mouse.Time);
                }
            }
            catch (Exception exception)
            {
                _ = exception;
#if DEBUG
                Console.Error.WriteLine($"마우스 훅 처리 실패: {exception}");
#endif
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    internal void ProcessLeftButtonDown(ScreenPoint point, uint time)
    {
        Clicked?.Invoke(this, point);
        uint elapsed = unchecked(time - _lastClickTime);
        bool isDoubleClick = _lastClickTime != 0
            && elapsed <= _doubleClickTime
            && Math.Abs(point.X - _lastClickPoint.X) <= _doubleClickHalfWidth
            && Math.Abs(point.Y - _lastClickPoint.Y) <= _doubleClickHalfHeight
            && point.TargetWindow != IntPtr.Zero
            && point.TargetWindow == _lastClickWindow;

        if (isDoubleClick)
        {
            _lastClickTime = 0;
            _lastClickWindow = IntPtr.Zero;
            DoubleClicked?.Invoke(this, point);
            return;
        }

        _lastClickTime = time;
        _lastClickPoint = new NativePoint { X = point.X, Y = point.Y };
        _lastClickWindow = point.TargetWindow;
    }

    public readonly record struct ScreenPoint(int X, int Y,
        IntPtr TargetWindow);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseData
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId,
        HookProcedure callback, IntPtr instance, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code,
        IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message,
        IntPtr window, uint min, uint max);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message,
        IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message,
        IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
