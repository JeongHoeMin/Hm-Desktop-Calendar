using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.DesktopIntegration;
using HmDesktopCalendar.Todos;
using HmDesktopCalendar.ViewModels;

namespace HmDesktopCalendar.RegressionTests;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("바탕화면 클릭 대상만 허용한다", DesktopSurfaceTargetsOnly),
            ("서로 다른 창의 클릭은 더블클릭이 아니다", DoubleClickRequiresSameTarget),
            ("Bounds 적용은 Z 순서를 보존한다", BoundsDoesNotChangeLayer),
            ("레이어 적용은 Bounds를 보존한다", LayerDoesNotChangeBounds),
            ("수정 모드는 영구 Topmost를 사용하지 않는다", ForegroundEditingIsNotTopmost),
            ("저장 전환은 두 불변식을 함께 만족한다", CommitPreservesBothInvariants),
            ("레이어 실패 시 저장 모드를 완료하지 않는다", FailureRollsBackToEditing),
            ("상태 점검은 손상된 책임만 복구한다", MaintenanceRepairsOnlyBrokenInvariant),
            ("취소는 원래 Bounds와 레이어를 함께 복원한다", CancelRestoresOriginalBounds),
            ("Explorer 재시작은 새 호스트에 정확히 재부착한다", ExplorerRestartReattaches),
            ("화면과 겹치는 음수 좌표는 복구하지 않는다", VisibleNegativeBoundsArePreserved),
            ("달력 ViewModel이 연도와 월을 독립적으로 이동한다", CalendarViewModelMovesPredictably),
            ("동기화 실패가 마지막 성공 시각을 보존한다", SynchronizationStatePreservesLastSuccess)
        };
        int failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine($"PASS: {test.Name}"); }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}\n{exception}");
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static void CalendarViewModelMovesPredictably()
    {
        var viewModel = CreateCalendarViewModel();
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2026 && viewModel.CurrentMonth == 7,
            "초기 표시 월이 주입한 오늘과 다릅니다.");

        viewModel.SelectYearAsync(2030).GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2030 && viewModel.CurrentMonth == 7,
            "연도 선택이 현재 월을 보존하지 않았습니다.");

        viewModel.SelectMonthAsync(2).GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2030 && viewModel.CurrentMonth == 2,
            "월 선택이 현재 연도를 보존하지 않았습니다.");

        viewModel.PreviousMonthAsync().GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2030 && viewModel.CurrentMonth == 1,
            "이전 달 이동 결과가 잘못되었습니다.");
        viewModel.PreviousMonthAsync().GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2029 && viewModel.CurrentMonth == 12,
            "이전 달 이동이 연도 경계를 처리하지 못했습니다.");

        viewModel.GoToTodayAsync().GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2026 && viewModel.CurrentMonth == 7,
            "오늘 바로가기가 주입한 오늘로 돌아오지 않았습니다.");
    }

    private static void SynchronizationStatePreservesLastSuccess()
    {
        var viewModel = CreateCalendarViewModel();
        viewModel.SetSynchronizationAvailability(false);
        Assert(viewModel.SynchronizationStatus == "로컬 전용",
            "로그아웃 상태가 로컬 전용으로 표시되지 않았습니다.");

        viewModel.SetSynchronizationAvailability(true);
        viewModel.ApplySynchronizationState(new TodoSynchronizationState(
            TodoSynchronizationStatus.InProgress,
            new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.Zero)));
        Assert(viewModel.IsSynchronizing &&
               viewModel.SynchronizationStatus == "동기화 중…",
            "진행 상태가 올바르게 표시되지 않았습니다.");

        var succeededAt = new DateTimeOffset(2026, 7, 14, 12, 34, 0,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 7, 14)));
        viewModel.ApplySynchronizationState(new TodoSynchronizationState(
            TodoSynchronizationStatus.Succeeded, succeededAt));
        Assert(!viewModel.IsSynchronizing &&
               viewModel.SynchronizationStatus.Contains("마지막 성공 12:34"),
            "마지막 성공 시각이 표시되지 않았습니다.");

        viewModel.ApplySynchronizationState(new TodoSynchronizationState(
            TodoSynchronizationStatus.Failed, succeededAt.AddMinutes(2),
            "network"));
        Assert(viewModel.IsSynchronizationFailed &&
               viewModel.SynchronizationStatus.Contains("동기화 실패") &&
               viewModel.SynchronizationStatus.Contains("마지막 성공 12:34"),
            "실패 상태가 마지막 성공 시각을 보존하지 않았습니다.");

        viewModel.SetSynchronizationAvailability(false);
        viewModel.SetSynchronizationAvailability(true);
        Assert(!viewModel.SynchronizationStatus.Contains("12:34"),
            "로그아웃 뒤 이전 계정의 성공 시각이 남았습니다.");
    }

    private static CalendarViewModel CreateCalendarViewModel() => new(
        new EmptyTodoRepository(),
        () => new DateTime(2026, 7, 14),
        update =>
        {
            update();
            return Task.CompletedTask;
        });

    private static void BoundsDoesNotChangeLayer()
    {
        var native = new FakeWindowNativeApi();
        native.Parents[native.Calendar] = native.Host;
        native.NextWindows[native.IconView] = native.Calendar;
        var service = new WindowBoundsService(native);
        var target = new ScreenWindowBounds(-880, 337, 1080, 969);

        Assert(service.ApplyAttached(native.Calendar, native.Host, target,
            out _), "Bounds 적용이 실패했습니다.");
        NativePositionCall call = native.PositionCalls.Last();
        Assert(call.Flags.HasFlag(WindowPositionFlags.NoZOrder),
            "Bounds 적용에 SWP_NOZORDER가 없습니다.");
        Assert(call.InsertAfter == IntPtr.Zero,
            "Bounds 서비스가 레이어 핸들을 사용했습니다.");
        Assert(native.Bounds[native.Calendar] == target,
            "음수 화면 Bounds가 정확히 유지되지 않았습니다.");
        Assert(native.NextWindows[native.IconView] == native.Calendar,
            "Bounds 적용이 기존 Z 순서를 변경했습니다.");
    }

    private static void LayerDoesNotChangeBounds()
    {
        var native = new FakeWindowNativeApi();
        native.Parents[native.Calendar] = native.Host;
        var original = new ScreenWindowBounds(-880, 337, 1080, 969);
        native.Bounds[native.Calendar] = original;
        var service = new DesktopLayerService(native);
        var shell = new DesktopShellHandles(native.Host, native.IconView);

        Assert(service.EnsureIconsAboveCalendar(native.Calendar, shell,
            out _), "레이어 적용이 실패했습니다.");
        NativePositionCall call = native.PositionCalls.Last();
        Assert(call.Flags.HasFlag(WindowPositionFlags.NoMove) &&
               call.Flags.HasFlag(WindowPositionFlags.NoSize),
            "레이어 적용이 이동·크기 보존 플래그를 사용하지 않았습니다.");
        Assert(!call.Flags.HasFlag(WindowPositionFlags.NoZOrder),
            "레이어 적용이 Z 순서 변경을 차단했습니다.");
        Assert(native.Bounds[native.Calendar] == original,
            "레이어 적용이 Bounds를 변경했습니다.");
        Assert(service.AreIconsAboveCalendar(native.Calendar, shell),
            "아이콘이 달력 위에 배치되지 않았습니다.");
    }

    private static void ForegroundEditingIsNotTopmost()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결에 실패했습니다.");
        setup.Native.PositionCalls.Clear();

        WindowTransitionResult result =
            setup.Coordinator.BeginForegroundEditing();

        Assert(result.Success, result.Message);
        NativePositionCall activation = setup.Native.PositionCalls.Last();
        Assert(activation.InsertAfter == NativeWindowHandles.Top,
            "수정 창 활성화에 HWND_TOPMOST가 사용되었습니다.");
        Assert(activation.InsertAfter != NativeWindowHandles.Topmost,
            "수정 창이 영구 Topmost 상태가 되었습니다.");
    }

    private static void CommitPreservesBothInvariants()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결이 실패했습니다.");

        for (int index = 0; index < 3; index++)
        {
            Assert(setup.Coordinator.BeginForegroundEditing().Success,
                "전경 수정 모드 전환이 실패했습니다.");
            var desired = new ScreenWindowBounds(-880 + index * 25,
                337 + index * 30, 1080, 969);
            setup.Native.Bounds[setup.Native.Calendar] = desired;
            PixelRectLike(setup.Coordinator.CaptureCurrentBounds(), desired);
            WindowTransitionResult result =
                setup.Coordinator.TryCommitDesktop(desired.ToPixelRect());
            Assert(result.Success, result.Message);
            Assert(setup.Native.Bounds[setup.Native.Calendar] == desired,
                "저장 후 Bounds가 이동했습니다.");
            Assert(result.State.IconsAboveCalendar,
                "저장 후 달력이 아이콘 위로 올라왔습니다.");
            Assert(result.State.ParentAttached && result.State.BoundsMatch,
                "저장 후 부모 또는 Bounds 불변식이 깨졌습니다.");
        }
    }

    private static void FailureRollsBackToEditing()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결이 실패했습니다.");
        Assert(setup.Coordinator.BeginForegroundEditing().Success,
            "전경 수정 모드 전환이 실패했습니다.");
        var desired = new ScreenWindowBounds(-700, 500, 900, 800);
        setup.Native.Bounds[setup.Native.Calendar] = desired;
        setup.Native.FailLayerChange = true;

        WindowTransitionResult failed =
            setup.Coordinator.TryCommitDesktop(desired.ToPixelRect());
        Assert(!failed.Success, "레이어 실패인데 저장 전환이 성공했습니다.");
        Assert(setup.Coordinator.Mode ==
               DesktopCalendarWindowMode.ForegroundEditing,
            "실패 후 수정 모드가 유지되지 않았습니다.");
        Assert(setup.Native.Parents[setup.Native.Calendar] == IntPtr.Zero,
            "실패 후 전경 창으로 롤백되지 않았습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == desired,
            "실패 롤백 과정에서 Bounds가 변경되었습니다.");

        setup.Native.FailLayerChange = false;
        WindowTransitionResult retried =
            setup.Coordinator.TryCommitDesktop(desired.ToPixelRect());
        Assert(retried.Success, "재시도 저장이 실패했습니다.");
    }

    private static void VisibleNegativeBoundsArePreserved()
    {
        var native = new FakeWindowNativeApi();
        var service = new WindowBoundsService(native);
        var visible = new ScreenWindowBounds(-880, 337, 1080, 969);
        Assert(service.RecoverIfFullyOffscreen(visible, 700, 480) == visible,
            "화면과 겹치는 음수 좌표가 보정되었습니다.");
        var outside = new ScreenWindowBounds(-5000, -5000, 900, 700);
        Assert(service.RecoverIfFullyOffscreen(outside, 700, 480) != outside,
            "완전히 화면 밖인 Bounds가 복구되지 않았습니다.");
    }

    private static void MaintenanceRepairsOnlyBrokenInvariant()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결이 실패했습니다.");
        ScreenWindowBounds expected =
            setup.Native.Bounds[setup.Native.Calendar];

        setup.Native.PositionCalls.Clear();
        setup.Native.Bounds[setup.Native.Calendar] = expected with
            { X = expected.X + 150 };
        DesktopWindowState boundsRepair =
            setup.Coordinator.MaintainDesktopState();
        Assert(boundsRepair.IsValid, "Bounds 단독 복구가 실패했습니다.");
        Assert(setup.Native.PositionCalls.Any(call =>
                call.Flags.HasFlag(WindowPositionFlags.NoZOrder) &&
                !call.Flags.HasFlag(WindowPositionFlags.NoMove)),
            "Bounds 손상에 Bounds 서비스가 사용되지 않았습니다.");
        Assert(setup.Native.PositionCalls.All(call =>
                call.InsertAfter != setup.Native.IconView),
            "Bounds 단독 복구가 레이어를 변경했습니다.");

        setup.Native.PositionCalls.Clear();
        setup.Native.NextWindows[setup.Native.IconView] = IntPtr.Zero;
        DesktopWindowState layerRepair =
            setup.Coordinator.MaintainDesktopState();
        Assert(layerRepair.IsValid, "레이어 단독 복구가 실패했습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == expected,
            "레이어 단독 복구가 Bounds를 변경했습니다.");
        Assert(setup.Native.PositionCalls.Any(call =>
                call.InsertAfter == setup.Native.IconView &&
                call.Flags.HasFlag(WindowPositionFlags.NoMove) &&
                call.Flags.HasFlag(WindowPositionFlags.NoSize)),
            "레이어 손상에 레이어 서비스가 사용되지 않았습니다.");

        setup.Native.Parents[setup.Native.Calendar] = IntPtr.Zero;
        DesktopWindowState parentRepair =
            setup.Coordinator.MaintainDesktopState();
        Assert(parentRepair.IsValid, "부모 손상 전체 복구가 실패했습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == expected,
            "부모 재연결 과정에서 Bounds가 변경되었습니다.");
    }

    private static void CancelRestoresOriginalBounds()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결에 실패했습니다.");
        ScreenWindowBounds original = setup.Native.Bounds[setup.Native.Calendar];
        Assert(setup.Coordinator.BeginForegroundEditing().Success,
            "전경 수정 모드 전환에 실패했습니다.");
        setup.Native.Bounds[setup.Native.Calendar] =
            new ScreenWindowBounds(-430, 610, 820, 740);

        WindowTransitionResult cancelled =
            setup.Coordinator.TryCommitDesktop(original.ToPixelRect());

        Assert(cancelled.Success && cancelled.State.IsValid,
            "취소 전환이 모든 불변식을 만족하지 못했습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == original,
            "취소 후 원래 Bounds가 정확히 복원되지 않았습니다.");
    }

    private static void ExplorerRestartReattaches()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결에 실패했습니다.");
        ScreenWindowBounds expected = setup.Native.Bounds[setup.Native.Calendar];
        setup.Native.ActiveHost = setup.Native.RestartedHost;
        setup.Native.ActiveIconView = setup.Native.RestartedIconView;
        setup.Native.Parents[setup.Native.RestartedIconView] =
            setup.Native.RestartedHost;

        DesktopWindowState repaired = setup.Coordinator.MaintainDesktopState();

        Assert(repaired.IsValid, "Explorer 재시작 후 불변식 복구에 실패했습니다.");
        Assert(setup.Native.Parents[setup.Native.Calendar] ==
               setup.Native.RestartedHost,
            "달력이 새 Explorer 호스트에 연결되지 않았습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == expected,
            "Explorer 재부착 중 Bounds가 변경되었습니다.");
        Assert(setup.Native.NextWindows[setup.Native.RestartedIconView] ==
               setup.Native.Calendar,
            "새 아이콘 뷰가 달력 위에 배치되지 않았습니다.");
    }

    private static void DesktopSurfaceTargetsOnly()
    {
        var native = new FakeWindowNativeApi();
        var tester = new DesktopSurfaceHitTester(native,
            new DesktopShellLocator(native));
        IntPtr desktopChild = new(35);
        IntPtr otherWindow = new(60);
        IntPtr otherChild = new(61);
        native.Parents[desktopChild] = native.IconView;
        native.Parents[native.Calendar] = native.Host;
        native.Parents[otherChild] = otherWindow;

        Assert(tester.IsDesktopSurface(native.Host),
            "바탕화면 호스트를 거부했습니다.");
        Assert(tester.IsDesktopSurface(native.IconView),
            "바탕화면 뷰를 거부했습니다.");
        Assert(tester.IsDesktopSurface(desktopChild),
            "바탕화면 뷰의 자식 창을 거부했습니다.");
        Assert(!tester.IsDesktopSurface(native.Calendar),
            "달력 창을 바탕화면으로 잘못 판정했습니다.");
        Assert(!tester.IsDesktopSurface(otherWindow) &&
               !tester.IsDesktopSurface(otherChild),
            "다른 앱 창을 바탕화면으로 잘못 판정했습니다.");
        Assert(!tester.IsDesktopSurface(IntPtr.Zero),
            "빈 창 핸들을 허용했습니다.");
        native.InvalidWindows.Add(desktopChild);
        Assert(!tester.IsDesktopSurface(desktopChild),
            "유효하지 않은 창 핸들을 허용했습니다.");

        native.ActiveIconView = IntPtr.Zero;
        Assert(!tester.IsDesktopSurface(native.Host),
            "Explorer 뷰를 찾지 못했는데 클릭을 허용했습니다.");
    }

    private static void DoubleClickRequiresSameTarget()
    {
        var native = new FakeWindowNativeApi();
        using var monitor = new GlobalPointerMonitor(native);
        int doubleClicks = 0;
        monitor.DoubleClicked += (_, _) => doubleClicks++;

        monitor.ProcessLeftButtonDown(new GlobalPointerMonitor.ScreenPoint(
            100, 100, new IntPtr(60)), 100);
        monitor.ProcessLeftButtonDown(new GlobalPointerMonitor.ScreenPoint(
            100, 100, native.IconView), 110);
        Assert(doubleClicks == 0,
            "서로 다른 대상 창의 클릭을 더블클릭으로 판정했습니다.");

        monitor.Stop();
        monitor.ProcessLeftButtonDown(new GlobalPointerMonitor.ScreenPoint(
            100, 100, native.IconView), 100);
        monitor.ProcessLeftButtonDown(new GlobalPointerMonitor.ScreenPoint(
            100, 100, native.IconView), 110);
        Assert(doubleClicks == 1,
            "동일한 대상 창의 정상 더블클릭을 감지하지 못했습니다.");
    }

    private static (DesktopCalendarWindowCoordinator Coordinator,
        FakeWindowNativeApi Native) CreateCoordinator()
    {
        var native = new FakeWindowNativeApi();
        var locator = new DesktopShellLocator(native);
        var bounds = new WindowBoundsService(native);
        var layer = new DesktopLayerService(native);
        var attachment = new DesktopAttachmentService(native);
        var initial = new ScreenWindowBounds(-880, 337, 1080, 969);
        var coordinator = new DesktopCalendarWindowCoordinator(
            () => native.Calendar, initial, 700, 480, native, locator,
            bounds, layer, attachment);
        return (coordinator, native);
    }

    private static void PixelRectLike(Avalonia.PixelRect actual,
        ScreenWindowBounds expected) => Assert(actual.X == expected.X &&
        actual.Y == expected.Y && actual.Width == expected.Width &&
        actual.Height == expected.Height, "캡처 Bounds가 일치하지 않습니다.");

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class EmptyTodoRepository : ITodoRepository
{
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<TodoItem>> GetByDateAsync(DateOnly date,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TodoItem>>([]);

    public Task<IReadOnlyList<TodoItem>> GetByRangeAsync(DateOnly from,
        DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TodoItem>>([]);

    public Task UpsertAsync(TodoItem item,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteAsync(Guid id,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal readonly record struct NativePositionCall(IntPtr Window,
    IntPtr InsertAfter, int X, int Y, int Width, int Height,
    WindowPositionFlags Flags);

internal sealed class FakeWindowNativeApi : IWindowNativeApi
{
    public IntPtr Calendar { get; } = new(10);
    public IntPtr Host { get; } = new(20);
    public IntPtr IconView { get; } = new(30);
    public IntPtr Progman { get; } = new(40);
    public IntPtr RestartedHost { get; } = new(21);
    public IntPtr RestartedIconView { get; } = new(31);
    public IntPtr ActiveHost { get; set; }
    public IntPtr ActiveIconView { get; set; }
    public Dictionary<IntPtr, IntPtr> Parents { get; } = [];
    public Dictionary<IntPtr, IntPtr> NextWindows { get; } = [];
    public Dictionary<IntPtr, ScreenWindowBounds> Bounds { get; } = [];
    public List<NativePositionCall> PositionCalls { get; } = [];
    public HashSet<IntPtr> InvalidWindows { get; } = [];
    public bool FailLayerChange { get; set; }
    public IntPtr WindowAtPoint { get; set; }
    private readonly Dictionary<IntPtr, long> _styles = [];
    private readonly Dictionary<IntPtr, long> _extendedStyles = [];
    private readonly IntPtr _monitor = new(50);
    private const int HostOriginX = -1080;
    private const int HostOriginY = -383;

    public FakeWindowNativeApi()
    {
        ActiveHost = Host;
        ActiveIconView = IconView;
        Parents[Calendar] = IntPtr.Zero;
        Parents[IconView] = Host;
        Bounds[Calendar] = new ScreenWindowBounds(-880, 337, 1080, 969);
    }

    public IntPtr FindTopLevelWindow(string className) =>
        className == "Progman" ? Progman : IntPtr.Zero;
    public IntPtr FindChildWindow(IntPtr parent, string className,
        string? windowName = null) =>
        parent == ActiveHost && className == "SHELLDLL_DefView"
            ? ActiveIconView : IntPtr.Zero;
    public IReadOnlyList<IntPtr> EnumerateTopLevelWindows() =>
        [ActiveHost, Progman];
    public void RequestDesktopWorkerWindow(IntPtr progman) { }
    public IntPtr WindowFromPoint(int screenX, int screenY) => WindowAtPoint;
    public bool IsWindow(IntPtr window) => window != IntPtr.Zero &&
        !InvalidWindows.Contains(window);
    public IntPtr GetParent(IntPtr window) =>
        Parents.TryGetValue(window, out IntPtr parent) ? parent : IntPtr.Zero;
    public IntPtr GetNextWindow(IntPtr window) =>
        NextWindows.TryGetValue(window, out IntPtr next) ? next : IntPtr.Zero;
    public bool TrySetParent(IntPtr child, IntPtr parent, out int error)
    {
        Parents[child] = parent;
        error = 0;
        return true;
    }
    public long GetStyle(IntPtr window) => _styles.GetValueOrDefault(window);
    public long GetExtendedStyle(IntPtr window) =>
        _extendedStyles.GetValueOrDefault(window);
    public void SetStyle(IntPtr window, long style) => _styles[window] = style;
    public void SetExtendedStyle(IntPtr window, long style) =>
        _extendedStyles[window] = style;

    public bool SetWindowPosition(IntPtr window, IntPtr insertAfter, int x,
        int y, int width, int height, WindowPositionFlags flags,
        out int error)
    {
        PositionCalls.Add(new NativePositionCall(window, insertAfter, x, y,
            width, height, flags));
        if (FailLayerChange && insertAfter == ActiveIconView &&
            flags.HasFlag(WindowPositionFlags.NoMove) &&
            flags.HasFlag(WindowPositionFlags.NoSize))
        {
            error = 5;
            return false;
        }
        if (!flags.HasFlag(WindowPositionFlags.NoMove) ||
            !flags.HasFlag(WindowPositionFlags.NoSize))
        {
            ScreenWindowBounds current = Bounds.GetValueOrDefault(window);
            int screenX = x;
            int screenY = y;
            if (GetParent(window) == ActiveHost)
            {
                screenX += HostOriginX;
                screenY += HostOriginY;
            }
            Bounds[window] = new ScreenWindowBounds(
                flags.HasFlag(WindowPositionFlags.NoMove) ? current.X : screenX,
                flags.HasFlag(WindowPositionFlags.NoMove) ? current.Y : screenY,
                flags.HasFlag(WindowPositionFlags.NoSize) ? current.Width : width,
                flags.HasFlag(WindowPositionFlags.NoSize) ? current.Height : height);
        }
        if (!flags.HasFlag(WindowPositionFlags.NoZOrder) &&
            insertAfter == ActiveIconView)
        {
            NextWindows[ActiveIconView] = Calendar;
            NextWindows[Calendar] = IntPtr.Zero;
        }
        error = 0;
        return true;
    }

    public bool TryGetWindowRect(IntPtr window, out NativeWindowRect bounds)
    {
        if (!Bounds.TryGetValue(window, out ScreenWindowBounds value))
        {
            bounds = default;
            return false;
        }
        bounds = value.ToNativeRect();
        return true;
    }

    public bool TryScreenToClient(IntPtr window, int screenX, int screenY,
        out int clientX, out int clientY)
    {
        clientX = screenX - HostOriginX;
        clientY = screenY - HostOriginY;
        return window == ActiveHost;
    }
    public bool ShowWindow(IntPtr window) => true;
    public IntPtr MonitorFromRect(NativeWindowRect bounds, bool nearest)
    {
        bool intersects = bounds.Right > -1080 && bounds.Left < 0 &&
            bounds.Bottom > -383 && bounds.Top < 1537;
        return intersects || nearest ? _monitor : IntPtr.Zero;
    }
    public bool TryGetMonitorWorkArea(IntPtr monitor,
        out NativeMonitorWorkArea workArea)
    {
        workArea = new NativeMonitorWorkArea(-1080, -383, 0, 1489);
        return monitor == _monitor;
    }
}
