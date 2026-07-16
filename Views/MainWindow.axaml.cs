using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Avalonia.Input;
using HmDesktopCalendar.DesktopIntegration;
using HmDesktopCalendar.ViewModels;

namespace HmDesktopCalendar.Views;

public partial class MainWindow : Window
{
    public const int CalendarWidth = 980;
    public const int CalendarHeight = 680;
    public const int MinCalendarWidth = 700;
    public const int MinCalendarHeight = 480;
    private readonly HashSet<Border> _dayCells = [];
    private readonly MenuFlyout _menuFlyout = new();
    private readonly Flyout _dateFlyout = new()
    {
        Placement = PlacementMode.BottomEdgeAlignedLeft,
        ShowMode = FlyoutShowMode.Standard
    };
    private DatePickerMode? _openDatePicker;
    private DatePickerMode? _lastClosedDatePicker;
    private long _datePickerClosedAt;
    private long _menuClosedAt;
    private bool _isBoundsEditing;
    public MainWindow()
    {
        InitializeComponent();
        _menuFlyout.Closed += (_, _) =>
            _menuClosedAt = Stopwatch.GetTimestamp();
        _dateFlyout.Closed += (_, _) =>
        {
            _lastClosedDatePicker = _openDatePicker;
            _datePickerClosedAt = Stopwatch.GetTimestamp();
            _openDatePicker = null;
        };
    }

    public CalendarHit HitTestPoint(Point point)
    {
        if (Contains(MenuButton, point)) return new(CalendarHitTarget.Menu);
        if (Contains(PreviousButton, point)) return new(CalendarHitTarget.PreviousMonth);
        if (Contains(YearButton, point)) return new(CalendarHitTarget.YearPicker);
        if (Contains(MonthButton, point)) return new(CalendarHitTarget.MonthPicker);
        if (Contains(NextButton, point)) return new(CalendarHitTarget.NextMonth);
        foreach (Border cell in _dayCells)
            if (cell.Tag is DateOnly date && Contains(cell, point)) return new(CalendarHitTarget.Date, date);
        return new(CalendarHitTarget.None);
    }
    public void SetBoundsEditing(bool editing)
    {
        _isBoundsEditing = editing;
        MoveBar.IsVisible = editing;
        HeaderControls.IsVisible = !editing;
        SynchronizationPanel.IsVisible = !editing;
        ResizeOverlay.IsVisible = editing;
        CanResize = editing;
        if (editing) SetBoundsEditError(null);
    }
    public void SetBoundsEditError(string? message)
    {
        MoveBarText.Text = string.IsNullOrWhiteSpace(message)
            ? "☰  드래그하여 위치 이동"
            : $"저장 실패: {message}";
        MoveBarText.Foreground = string.IsNullOrWhiteSpace(message)
            ? Avalonia.Media.Brushes.White
            : Avalonia.Media.Brushes.LightYellow;
    }
    private void MoveBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isBoundsEditing ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        BeginMoveDrag(e);
        e.Handled = true;
    }
    private void ResizeHandle_OnPointerPressed(object? sender,
        PointerPressedEventArgs e)
    {
        if (!_isBoundsEditing ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            (sender as Control)?.Tag is not string edgeName ||
            !Enum.TryParse(edgeName, out WindowEdge edge))
            return;
        BeginResizeDrag(edge, e);
        e.Handled = true;
    }
    private void CalendarItems_OnSizeChanged(object? sender,
        SizeChangedEventArgs e)
    {
        if (DataContext is not CalendarViewModel viewModel) return;
        double cellHeight = CalendarItems.Bounds.Height / 6.0;
        int capacity = Math.Max(1, (int)Math.Floor((cellHeight - 26) / 16));
        viewModel.SetTaskRowCapacity(capacity);
    }
    public void ShowMenu(bool loggedIn, bool moving, Action overview,
        Action login, Action logout, Action move, Action complete, Action cancel)
    {
        if (_menuFlyout.IsOpen)
        {
            _menuFlyout.Hide();
            return;
        }
        if (WasJustClosed(_menuClosedAt))
        {
            _menuClosedAt = 0;
            return;
        }
        if (_dateFlyout.IsOpen) _dateFlyout.Hide();

        _menuFlyout.Items.Clear();
        var scheduleOverview = new MenuItem { Header = "일정 모아보기" };
        scheduleOverview.Click += (_, _) => overview();
        _menuFlyout.Items.Add(scheduleOverview);
        _menuFlyout.Items.Add(new Separator());
        var auth = new MenuItem { Header = loggedIn ? "로그아웃" : "로그인 / 회원가입" };
        auth.Click += (_, _) => { if (loggedIn) logout(); else login(); };
        _menuFlyout.Items.Add(auth);
        _menuFlyout.Items.Add(new Separator());
        if (!moving)
        {
            var item = new MenuItem { Header = "달력 위치 및 크기 수정" };
            item.Click += (_, _) => move();
            _menuFlyout.Items.Add(item);
        }
        else
        {
            var done = new MenuItem { Header = "위치 및 크기 저장" };
            done.Click += (_, _) => complete();
            _menuFlyout.Items.Add(done);
            var undo = new MenuItem { Header = "위치 및 크기 수정 취소" };
            undo.Click += (_, _) => cancel();
            _menuFlyout.Items.Add(undo);
        }
        _menuFlyout.ShowAt(MenuButton);
    }

    public void ShowYearPicker()
    {
        if (_menuFlyout.IsOpen) _menuFlyout.Hide();
        if (!PrepareDatePicker(DatePickerMode.Year) ||
            DataContext is not CalendarViewModel viewModel) return;

        var years = new StackPanel { Spacing = 2 };
        for (int year = viewModel.CurrentYear - 10;
             year <= viewModel.CurrentYear + 10; year++)
        {
            int selectedYear = year;
            var button = new Button
            {
                Content = $"{year}년",
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinWidth = 132
            };
            if (year == viewModel.CurrentYear) button.Classes.Add("secondary");
            button.Click += (_, _) => RunDateNavigation(() =>
                viewModel.SelectYearAsync(selectedYear));
            years.Children.Add(button);
        }

        _dateFlyout.Content = CreateDatePickerContent(
            new ScrollViewer { Content = years, MaxHeight = 300 });
        _dateFlyout.ShowAt(YearButton);
    }

    public void ShowMonthPicker()
    {
        if (_menuFlyout.IsOpen) _menuFlyout.Hide();
        if (!PrepareDatePicker(DatePickerMode.Month) ||
            DataContext is not CalendarViewModel viewModel) return;

        var months = new UniformGrid { Columns = 3, Rows = 4 };
        for (int month = 1; month <= 12; month++)
        {
            int selectedMonth = month;
            var button = new Button
            {
                Content = $"{month}월",
                Margin = new Thickness(2),
                MinWidth = 64
            };
            if (month == viewModel.CurrentMonth) button.Classes.Add("secondary");
            button.Click += (_, _) => RunDateNavigation(() =>
                viewModel.SelectMonthAsync(selectedMonth));
            months.Children.Add(button);
        }

        _dateFlyout.Content = CreateDatePickerContent(months);
        _dateFlyout.ShowAt(MonthButton);
    }

    private bool PrepareDatePicker(DatePickerMode mode)
    {
        if (_dateFlyout.IsOpen)
        {
            bool samePicker = _openDatePicker == mode;
            _dateFlyout.Hide();
            _openDatePicker = null;
            if (samePicker) return false;
        }
        else if (_lastClosedDatePicker == mode &&
                 WasJustClosed(_datePickerClosedAt))
        {
            _datePickerClosedAt = 0;
            _lastClosedDatePicker = null;
            return false;
        }
        _openDatePicker = mode;
        return true;
    }

    public void DismissFlyouts()
    {
        if (_menuFlyout.IsOpen) _menuFlyout.Hide();
        if (_dateFlyout.IsOpen) _dateFlyout.Hide();
    }

    private static bool WasJustClosed(long timestamp) => timestamp != 0 &&
        Stopwatch.GetElapsedTime(timestamp) < TimeSpan.FromMilliseconds(200);

    private Control CreateDatePickerContent(Control picker)
    {
        var today = new Button
        {
            Content = "오늘로 이동",
            Classes = { "secondary" },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        today.Click += (_, _) =>
        {
            if (DataContext is CalendarViewModel viewModel)
                RunDateNavigation(viewModel.GoToTodayAsync);
        };
        var content = new StackPanel { Spacing = 8, MinWidth = 220 };
        content.Children.Add(picker);
        content.Children.Add(today);
        return content;
    }

    private async void RunDateNavigation(Func<System.Threading.Tasks.Task> navigate)
    {
        _dateFlyout.Hide();
        _openDatePicker = null;
        try { await navigate(); }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"달력 날짜 이동 실패: {exception}");
        }
    }
    private bool Contains(Control control, Point client)
    {
        Point? origin = control.TranslatePoint(default, this);
        return origin is { } point && new Rect(point.X, point.Y,
            control.Bounds.Width, control.Bounds.Height).Contains(client);
    }
    private void Day_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    { if (sender is Border border) _dayCells.Add(border); }
    private void Day_OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    { if (sender is Border border) _dayCells.Remove(border); }

    private enum DatePickerMode { Year, Month }
}
