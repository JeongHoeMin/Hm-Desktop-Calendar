using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using HmDesktopCalendar.Calendar;
using HmDesktopCalendar.ViewModels;

namespace HmDesktopCalendar.Views;

public partial class EditWindow : Window
{
    private bool _allowClose;
    private bool _promptOpen;
    private CalendarEditorViewModel ViewModel =>
        (CalendarEditorViewModel)DataContext!;

    public EditWindow() => InitializeComponent();

    public EditWindow(CalendarEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Opened += async (_, _) =>
        {
            try { await viewModel.LoadAsync(); }
            catch (Exception exception)
            {
                viewModel.ErrorMessage =
                    $"일정을 불러오지 못했습니다: {exception.Message}";
            }
        };
        Closing += OnClosing;
        Closed += (_, _) => viewModel.Dispose();
    }

    public async Task ShowDateAsync(DateOnly date)
    {
        if (ViewModel.HasUnsavedChanges &&
            !await ConfirmDiscardAsync()) return;
        await ViewModel.LoadDateAsync(date, true);
    }

    public void CloseWithoutConfirmation()
    {
        _allowClose = true;
        Close();
    }

    private async void New_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel.BeginNew()) return;
        if (await ConfirmDiscardAsync()) ViewModel.BeginNew(true);
    }

    private async void Edit_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if ((sender as Control)?.DataContext is not CalendarItem item) return;
        if (ViewModel.BeginEdit(item)) return;
        if (await ConfirmDiscardAsync()) ViewModel.BeginEdit(item, true);
    }

    private async void Save_OnClick(object? sender, RoutedEventArgs eventArgs) =>
        await ViewModel.SaveDraftAsync();

    private async void Cancel_OnClick(object? sender,
        RoutedEventArgs eventArgs)
    {
        if (!ViewModel.Draft.HasUnsavedChanges ||
            await ConfirmDiscardAsync()) ViewModel.CancelDraft();
    }

    private async void Delete_OnClick(object? sender,
        RoutedEventArgs eventArgs)
    {
        if ((sender as Control)?.DataContext is not CalendarItem item) return;
        bool confirmed = await ConfirmAsync("일정을 삭제할까요?",
            $"‘{item.Title}’ 일정을 삭제합니다. 이 작업은 동기화된 모든 환경에 " +
            "반영됩니다.", "삭제", true);
        if (confirmed) await ViewModel.DeleteAsync(item);
    }

    private void PresetTime_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        string? value = (sender as Button)?.Tag as string;
        ViewModel.Draft.TimeValue = value switch
        {
            "none" => null,
            "now" => DateTime.Now.TimeOfDay,
            _ when TimeSpan.TryParse(value, out TimeSpan time) => time,
            _ => ViewModel.Draft.TimeValue
        };
    }

    private void PaletteColor_OnClick(object? sender,
        RoutedEventArgs eventArgs)
    {
        if ((sender as Button)?.Tag is string color)
            ViewModel.Draft.SetPaletteColor(color);
    }

    private void BackgroundPaletteColor_OnClick(object? sender,
        RoutedEventArgs eventArgs)
    {
        if ((sender as Button)?.Tag is string color)
            ViewModel.Background.SetPaletteColor(color);
    }

    private void BackgroundReset_OnClick(object? sender,
        RoutedEventArgs eventArgs) => ViewModel.Background.Clear();

    private async void BackgroundSave_OnClick(object? sender,
        RoutedEventArgs eventArgs) => await ViewModel.SaveBackgroundAsync();

    private async void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_allowClose || !ViewModel.HasUnsavedChanges) return;
        eventArgs.Cancel = true;
        if (_promptOpen) return;
        _promptOpen = true;
        try
        {
            if (!await ConfirmDiscardAsync()) return;
            _allowClose = true;
            Close();
        }
        finally { _promptOpen = false; }
    }

    private Task<bool> ConfirmDiscardAsync() => ConfirmAsync(
        "변경 내용을 버릴까요?",
        "저장하지 않은 입력이 있습니다. 계속하면 변경 내용이 사라집니다.",
        "변경 버리기", true);

    private async Task<bool> ConfirmAsync(string title, string message,
        string confirmText, bool danger = false)
    {
        bool result = false;
        var cancel = new Button
        {
            Content = "계속 편집", MinWidth = 100, IsCancel = true
        };
        var confirm = new Button
        {
            Content = confirmText, MinWidth = 100, IsDefault = true
        };
        confirm.Classes.Add(danger ? "danger" : "primary");
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, confirm }
        };
        var content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 380
                },
                buttons
            }
        };
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };
        dialog.Classes.Add("dialog");
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        await dialog.ShowDialog(this);
        return result;
    }
}
