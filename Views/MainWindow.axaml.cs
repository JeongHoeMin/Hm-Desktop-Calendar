using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
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
    private bool _isBoundsEditing;
    public MainWindow() => InitializeComponent();

    public CalendarHit HitTestPoint(Point point)
    {
        if (Contains(MenuButton, point)) return new(CalendarHitTarget.Menu);
        if (Contains(PreviousButton, point)) return new(CalendarHitTarget.PreviousMonth);
        if (Contains(NextButton, point)) return new(CalendarHitTarget.NextMonth);
        foreach (Border cell in _dayCells)
            if (cell.Tag is DateOnly date && Contains(cell, point)) return new(CalendarHitTarget.Date, date);
        return new(CalendarHitTarget.None);
    }
    public void SetBoundsEditing(bool editing)
    {
        _isBoundsEditing = editing;
        MoveBar.IsVisible = editing;
        MonthTitle.IsVisible = !editing;
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
    public void ShowMenu(bool loggedIn, bool moving, Action login, Action logout,
        Action move, Action complete, Action cancel)
    {
        var menu = new MenuFlyout();
        var auth = new MenuItem { Header = loggedIn ? "로그아웃" : "로그인 / 회원가입" };
        auth.Click += (_, _) => { if (loggedIn) logout(); else login(); };
        menu.Items.Add(auth); menu.Items.Add(new Separator());
        if (!moving)
        {
            var item = new MenuItem { Header = "달력 위치 및 크기 수정" }; item.Click += (_, _) => move(); menu.Items.Add(item);
        }
        else
        {
            var done = new MenuItem { Header = "위치 및 크기 저장" }; done.Click += (_, _) => complete(); menu.Items.Add(done);
            var undo = new MenuItem { Header = "위치 및 크기 수정 취소" }; undo.Click += (_, _) => cancel(); menu.Items.Add(undo);
        }
        menu.ShowAt(MenuButton);
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
}
