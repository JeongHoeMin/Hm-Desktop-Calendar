using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace HmDesktopCalendar.DesktopIntegration;

public enum DesktopCalendarWindowMode
{
    Uninitialized,
    DesktopAttached,
    ForegroundEditing,
    Reattaching,
    Disposed
}

public sealed class DesktopCalendarWindowCoordinator : IDisposable
{
    private readonly Window? _window;
    private readonly Func<IntPtr> _handleProvider;
    private readonly IWindowNativeApi _native;
    private readonly DesktopShellLocator _shellLocator;
    private readonly WindowBoundsService _boundsService;
    private readonly DesktopLayerService _layerService;
    private readonly DesktopAttachmentService _attachmentService;
    private readonly DispatcherTimer? _timer;
    private readonly int _minimumWidth;
    private readonly int _minimumHeight;
    private ScreenWindowBounds _bounds;
    private IntPtr _handle;
    private bool _disposed;

    public DesktopCalendarWindowCoordinator(Window window,
        PixelRect initialBounds)
    {
        _window = window;
        _handleProvider = () =>
            window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        _native = new Win32WindowNativeApi();
        _shellLocator = new DesktopShellLocator(_native);
        _boundsService = new WindowBoundsService(_native);
        _layerService = new DesktopLayerService(_native);
        _attachmentService = new DesktopAttachmentService(_native);
        _minimumWidth = Math.Max(1, (int)Math.Ceiling(window.MinWidth));
        _minimumHeight = Math.Max(1, (int)Math.Ceiling(window.MinHeight));
        _bounds = _boundsService.RecoverIfFullyOffscreen(
            ScreenWindowBounds.FromPixelRect(initialBounds), _minimumWidth,
            _minimumHeight);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += OnTick;
        window.Opened += OnOpened;
        window.Closed += OnClosed;
    }

    public DesktopCalendarWindowCoordinator(Func<IntPtr> handleProvider,
        ScreenWindowBounds initialBounds, int minimumWidth, int minimumHeight,
        IWindowNativeApi native, DesktopShellLocator shellLocator,
        WindowBoundsService boundsService, DesktopLayerService layerService,
        DesktopAttachmentService attachmentService)
    {
        _handleProvider = handleProvider;
        _native = native;
        _shellLocator = shellLocator;
        _boundsService = boundsService;
        _layerService = layerService;
        _attachmentService = attachmentService;
        _minimumWidth = minimumWidth;
        _minimumHeight = minimumHeight;
        _bounds = _boundsService.RecoverIfFullyOffscreen(initialBounds,
            minimumWidth, minimumHeight);
    }

    public DesktopCalendarWindowMode Mode { get; private set; } =
        DesktopCalendarWindowMode.Uninitialized;
    public PixelRect Bounds => _bounds.ToPixelRect();

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer?.Start();
        if (_window?.IsVisible == true) InitializeDesktop();
    }

    public WindowTransitionResult InitializeDesktop()
    {
        _handle = _handleProvider();
        if (_handle == IntPtr.Zero)
            return WindowTransitionResult.Fail(
                WindowTransitionFailure.WindowUnavailable,
                "달력 창 핸들을 찾을 수 없습니다.");
        return TransitionToDesktop(_bounds, true);
    }

    public WindowTransitionResult BeginForegroundEditing()
    {
        if (Mode != DesktopCalendarWindowMode.DesktopAttached)
            return WindowTransitionResult.Fail(
                WindowTransitionFailure.InvalidMode,
                "달력이 바탕화면에 연결된 상태가 아닙니다.");
        if (!_boundsService.TryCapture(_handle, out ScreenWindowBounds current))
            return WindowTransitionResult.Fail(
                WindowTransitionFailure.WindowUnavailable,
                "현재 달력 위치를 읽지 못했습니다.");
        if (!_attachmentService.Detach(_handle, out int error))
            return WindowTransitionResult.Fail(
                WindowTransitionFailure.ParentChangeFailed,
                $"전경 창 분리 실패. Win32 오류: {error}");
        _bounds = current;
        Mode = DesktopCalendarWindowMode.ForegroundEditing;
        if (!_boundsService.ApplyTopLevel(_handle, current, out error) ||
            !_attachmentService.ActivateForeground(_handle, out error))
        {
            TransitionToDesktop(current, false);
            return WindowTransitionResult.Fail(
                WindowTransitionFailure.BoundsApplyFailed,
                $"전경 창 위치 적용 실패. Win32 오류: {error}");
        }
        return WindowTransitionResult.Ok(new DesktopWindowState(false, true,
            false, current));
    }

    public PixelRect CaptureCurrentBounds()
    {
        if (_boundsService.TryCapture(_handle, out ScreenWindowBounds current))
            _bounds = EnsureMinimumSize(current);
        return _bounds.ToPixelRect();
    }

    public WindowTransitionResult TryCommitDesktop(PixelRect targetBounds) =>
        TransitionToDesktop(EnsureMinimumSize(
            ScreenWindowBounds.FromPixelRect(targetBounds)), true);

    public Point ToClientPoint(int screenX, int screenY) =>
        _native.TryScreenToClient(_handle, screenX, screenY,
            out int clientX, out int clientY)
            ? new Point(clientX, clientY)
            : new Point(double.NaN, double.NaN);

    public DesktopWindowState Inspect(PixelRect expectedBounds) =>
        Inspect(ScreenWindowBounds.FromPixelRect(expectedBounds));

    private WindowTransitionResult TransitionToDesktop(
        ScreenWindowBounds target, bool rollbackOnFailure)
    {
        if (_handle == IntPtr.Zero) _handle = _handleProvider();
        if (_handle == IntPtr.Zero)
            return WindowTransitionResult.Fail(
                WindowTransitionFailure.WindowUnavailable,
                "달력 창 핸들을 찾을 수 없습니다.");
        Mode = DesktopCalendarWindowMode.Reattaching;
        if (!_shellLocator.TryLocate(true, out DesktopShellHandles shell))
            return FailAndRollback(WindowTransitionFailure.ShellUnavailable,
                "Explorer 아이콘 호스트를 찾지 못했습니다.", target,
                rollbackOnFailure);
        if (!_attachmentService.IsAttached(_handle, shell) &&
            !_attachmentService.Attach(_handle, shell, out int error))
            return FailAndRollback(WindowTransitionFailure.ParentChangeFailed,
                $"바탕화면 부모 연결 실패. Win32 오류: {error}", target,
                rollbackOnFailure);
        if (!_boundsService.ApplyAttached(_handle, shell.Host, target,
                out error))
            return FailAndRollback(WindowTransitionFailure.BoundsApplyFailed,
                $"달력 위치 적용 실패. Win32 오류: {error}", target,
                rollbackOnFailure);
        if (!_layerService.EnsureIconsAboveCalendar(_handle, shell,
                out error))
            return FailAndRollback(WindowTransitionFailure.LayerApplyFailed,
                $"아이콘 레이어 적용 실패. Win32 오류: {error}", target,
                rollbackOnFailure);
        DesktopWindowState state = Inspect(target, shell);
        if (!state.IsValid)
            return FailAndRollback(WindowTransitionFailure.ValidationFailed,
                BuildValidationMessage(state), target, rollbackOnFailure);
        _bounds = target;
        Mode = DesktopCalendarWindowMode.DesktopAttached;
        return WindowTransitionResult.Ok(state);
    }

    private WindowTransitionResult FailAndRollback(
        WindowTransitionFailure failure, string message,
        ScreenWindowBounds target, bool rollback)
    {
        DesktopWindowState state = Inspect(target);
        if (rollback)
        {
            _attachmentService.Detach(_handle, out _);
            _boundsService.ApplyTopLevel(_handle, target, out _);
            _attachmentService.ActivateForeground(_handle, out _);
            _bounds = target;
            Mode = DesktopCalendarWindowMode.ForegroundEditing;
        }
        else Mode = DesktopCalendarWindowMode.Uninitialized;
        return WindowTransitionResult.Fail(failure, message, state);
    }

    private DesktopWindowState Inspect(ScreenWindowBounds expected)
    {
        if (!_shellLocator.TryLocate(false, out DesktopShellHandles shell))
        {
            _boundsService.Matches(_handle, expected, out var actual);
            return new DesktopWindowState(false, actual == expected, false,
                actual);
        }
        return Inspect(expected, shell);
    }

    private DesktopWindowState Inspect(ScreenWindowBounds expected,
        DesktopShellHandles shell)
    {
        bool parent = _attachmentService.IsAttached(_handle, shell);
        bool boundsMatch = _boundsService.Matches(_handle, expected,
            out ScreenWindowBounds? actual);
        bool layer = parent &&
            _layerService.AreIconsAboveCalendar(_handle, shell);
        return new DesktopWindowState(parent, boundsMatch, layer, actual);
    }

    public DesktopWindowState MaintainDesktopState()
    {
        if (Mode == DesktopCalendarWindowMode.Uninitialized)
            return InitializeDesktop().State;
        if (Mode != DesktopCalendarWindowMode.DesktopAttached ||
            !_shellLocator.TryLocate(true, out DesktopShellHandles shell))
            return Inspect(_bounds);
        DesktopWindowState state = Inspect(_bounds, shell);
        if (!state.ParentAttached)
        {
            WindowTransitionResult result = TransitionToDesktop(_bounds, false);
            if (!result.Success) Console.Error.WriteLine(result.Message);
            return result.State;
        }
        if (!state.BoundsMatch)
            _boundsService.ApplyAttached(_handle, shell.Host, _bounds, out _);
        if (!state.IconsAboveCalendar)
            _layerService.EnsureIconsAboveCalendar(_handle, shell, out _);
        DesktopWindowState repaired = Inspect(_bounds, shell);
        if (!repaired.IsValid)
            Console.Error.WriteLine(BuildValidationMessage(repaired));
        return repaired;
    }

    private ScreenWindowBounds EnsureMinimumSize(ScreenWindowBounds bounds) =>
        _boundsService.EnsureMinimumSize(bounds, _minimumWidth, _minimumHeight);

    private static string BuildValidationMessage(DesktopWindowState state) =>
        $"달력 상태 검증 실패: 부모={state.ParentAttached}, " +
        $"좌표={state.BoundsMatch}, 아이콘우선={state.IconsAboveCalendar}";

    private void OnOpened(object? sender, EventArgs eventArgs) =>
        Dispatcher.UIThread.Post(() =>
        {
            WindowTransitionResult result = InitializeDesktop();
            if (!result.Success) Console.Error.WriteLine(result.Message);
        }, DispatcherPriority.Background);
    private void OnClosed(object? sender, EventArgs eventArgs) => Dispose();
    private void OnTick(object? sender, EventArgs eventArgs) =>
        MaintainDesktopState();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Mode = DesktopCalendarWindowMode.Disposed;
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
        if (_window is not null)
        {
            _window.Opened -= OnOpened;
            _window.Closed -= OnClosed;
        }
    }
}
