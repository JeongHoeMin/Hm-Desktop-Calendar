using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HmDesktopCalendar.Calendar;
using HmDesktopCalendar.ViewModels;

namespace HmDesktopCalendar.Views;

public sealed class ScheduleOverviewEditRequestedEventArgs(
    DateOnly date, CalendarItem? item) : EventArgs
{
    public DateOnly Date { get; } = date;
    public CalendarItem? Item { get; } = item;
}

public partial class ScheduleOverviewWindow : Window
{
    private ScheduleOverviewViewModel ViewModel =>
        (ScheduleOverviewViewModel)DataContext!;

    public event EventHandler<ScheduleOverviewEditRequestedEventArgs>?
        EditRequested;

    public ScheduleOverviewWindow() => InitializeComponent();

    public ScheduleOverviewWindow(ScheduleOverviewViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.InitializeAsync();
        Closed += (_, _) => viewModel.Dispose();
    }

    private void New_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        EditRequested?.Invoke(this, new ScheduleOverviewEditRequestedEventArgs(
            DateOnly.FromDateTime(DateTime.Today), null));

    private async void ExportIcs_OnClick(object? sender,
        RoutedEventArgs eventArgs)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "ICS 파일 저장",
                SuggestedFileName = $"하네스-일정-{DateTime.Today:yyyyMMdd}.ics",
                DefaultExtension = "ics",
                FileTypeChoices =
                [
                    new FilePickerFileType("iCalendar 파일")
                    {
                        Patterns = ["*.ics"],
                        MimeTypes = ["text/calendar"]
                    }
                ]
            });
        if (file is not null)
            await ViewModel.ExportIcsAsync(file.Path.LocalPath);
    }

    private void Edit_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if ((sender as Control)?.DataContext is not
            ScheduleOverviewItemViewModel occurrence) return;
        EditRequested?.Invoke(this, new ScheduleOverviewEditRequestedEventArgs(
            occurrence.Date, occurrence.Item));
    }
}
