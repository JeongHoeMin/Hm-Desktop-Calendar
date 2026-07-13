using System;
using System.Runtime.InteropServices;

namespace HmDesktopCalendar.DesktopIntegration;

public sealed class DesktopIconHitTester
{
    private const uint LvmFirst = 0x1000;
    private const uint LvmHitTest = LvmFirst + 18;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    public bool IsIconAtPoint(int screenX, int screenY)
    {
        if (!TryFindDesktopListView(out IntPtr listView))
            return false;

        var hitTest = new ListViewHitTestInfo
        {
            Point = new NativePoint(screenX, screenY),
            Item = -1,
            SubItem = 0,
            Group = 0
        };
        if (!ScreenToClient(listView, ref hitTest.Point))
            return false;

        GetWindowThreadProcessId(listView, out uint processId);
        if (processId == 0)
            return false;

        IntPtr process = OpenProcess(ProcessVmOperation | ProcessVmRead |
            ProcessVmWrite, false, processId);
        if (process == IntPtr.Zero)
            return false;

        int size = Marshal.SizeOf<ListViewHitTestInfo>();
        IntPtr remoteBuffer = IntPtr.Zero;
        try
        {
            remoteBuffer = VirtualAllocEx(process, IntPtr.Zero, (UIntPtr)size,
                MemCommit | MemReserve, PageReadWrite);
            if (remoteBuffer == IntPtr.Zero)
                return false;

            IntPtr localBuffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(hitTest, localBuffer, false);
                if (!WriteProcessMemory(process, remoteBuffer, localBuffer,
                        (UIntPtr)size, out _))
                    return false;

                IntPtr result = SendMessage(listView, LvmHitTest,
                    IntPtr.Zero, remoteBuffer);
                return result.ToInt64() >= 0;
            }
            finally
            {
                Marshal.FreeHGlobal(localBuffer);
            }
        }
        finally
        {
            if (remoteBuffer != IntPtr.Zero)
                VirtualFreeEx(process, remoteBuffer, UIntPtr.Zero, MemRelease);
            CloseHandle(process);
        }
    }

    private static bool TryFindDesktopListView(out IntPtr listView)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            IntPtr shellView = FindWindowEx(window, IntPtr.Zero,
                "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
                return true;

            found = FindWindowEx(shellView, IntPtr.Zero,
                "SysListView32", "FolderView");
            if (found == IntPtr.Zero)
                found = FindWindowEx(shellView, IntPtr.Zero,
                    "SysListView32", null);
            return found == IntPtr.Zero;
        }, IntPtr.Zero);

        listView = found;
        return listView != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ListViewHitTestInfo
    {
        public NativePoint Point;
        public uint Flags;
        public int Item;
        public int SubItem;
        public int Group;
    }

    private delegate bool EnumWindowsProcedure(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProcedure callback,
        IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter,
        string? className, string? windowName);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr window,
        ref NativePoint point);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window,
        out uint processId);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message,
        IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address,
        UIntPtr size, uint allocationType, uint protection);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address,
        UIntPtr size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr process,
        IntPtr address, IntPtr buffer, UIntPtr size, out UIntPtr written);
}
