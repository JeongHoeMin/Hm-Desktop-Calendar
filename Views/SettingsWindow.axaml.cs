using Avalonia.Controls;
using Avalonia.Interactivity;
using HmDesktopCalendar.ViewModels;

namespace HmDesktopCalendar.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(SettingsViewModel viewModel) : this() =>
        DataContext = viewModel;

    private void ResetWindowPosition_OnClick(object? sender,
        RoutedEventArgs eventArgs) =>
        ((SettingsViewModel)DataContext!).ResetWindowPosition();

    private void OpenBackupFolder_OnClick(object? sender,
        RoutedEventArgs eventArgs) =>
        ((SettingsViewModel)DataContext!).OpenBackupFolder();
}
