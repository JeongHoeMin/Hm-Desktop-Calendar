using System;

namespace HmDesktopCalendar.DesktopIntegration;

public sealed class DesktopSurfaceHitTester
{
    private const int MaximumParentDepth = 32;
    private readonly IWindowNativeApi _native;
    private readonly DesktopShellLocator _shellLocator;

    public DesktopSurfaceHitTester(IWindowNativeApi native,
        DesktopShellLocator shellLocator)
    {
        _native = native;
        _shellLocator = shellLocator;
    }

    public bool IsDesktopSurface(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero || !_native.IsWindow(targetWindow) ||
            !_shellLocator.TryLocate(false, out DesktopShellHandles shell))
            return false;

        if (targetWindow == shell.Host)
            return true;

        IntPtr current = targetWindow;
        for (int depth = 0;
             depth < MaximumParentDepth && current != IntPtr.Zero;
             depth++)
        {
            if (current == shell.IconView)
                return true;
            if (current == shell.Host)
                return false;

            IntPtr parent = _native.GetParent(current);
            if (parent == current)
                return false;
            current = parent;
        }

        return false;
    }
}
