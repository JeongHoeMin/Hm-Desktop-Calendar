using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HmDesktopCalendar.Reminders;

namespace HmDesktopCalendar.Views;

public enum ReminderWindowAction { Acknowledge, Snooze, Edit }

public sealed record ReminderWindowActionEventArgs(ReminderWindowAction Action,
    int SnoozeMinutes = 0);

public partial class ReminderWindow : Window
{
    private bool _actionRaised;
    public ReminderNotification Notification { get; } = null!;
    public string OccurrenceText => Notification.Item.StartTime is { } time
        ? $"{Notification.OccurrenceDate:yyyy년 M월 d일} {time:HH:mm}"
        : $"{Notification.OccurrenceDate:yyyy년 M월 d일}";
    public string StatusText => Notification.IsSnoozed ? "다시 알림" :
        Notification.IsRecovered ? "앱을 다시 시작해 놓친 알림을 표시했습니다."
        : "예정된 알림 시각입니다.";

    public event EventHandler<ReminderWindowActionEventArgs>? ActionRequested;

    public ReminderWindow() => InitializeComponent();

    public ReminderWindow(ReminderNotification notification) : this()
    {
        Notification = notification;
        DataContext = this;
        Closing += (_, _) =>
        {
            if (!_actionRaised) RaiseAction(ReminderWindowAction.Acknowledge);
        };
    }

    public void CloseSilently()
    {
        _actionRaised = true;
        Close();
    }

    private void Acknowledge_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        Complete(ReminderWindowAction.Acknowledge);

    private void Edit_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        Complete(ReminderWindowAction.Edit);

    private void Snooze_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if ((sender as Button)?.Tag is string value &&
            int.TryParse(value, out int minutes))
            Complete(ReminderWindowAction.Snooze, minutes);
    }

    private void Complete(ReminderWindowAction action, int minutes = 0)
    {
        RaiseAction(action, minutes);
        Close();
    }

    private void RaiseAction(ReminderWindowAction action, int minutes = 0)
    {
        if (_actionRaised) return;
        _actionRaised = true;
        ActionRequested?.Invoke(this,
            new ReminderWindowActionEventArgs(action, minutes));
    }
}
