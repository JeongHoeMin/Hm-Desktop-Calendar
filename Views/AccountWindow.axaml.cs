using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HmDesktopCalendar.ViewModels;

namespace HmDesktopCalendar.Views;

public partial class AccountWindow : Window
{
    private readonly Func<Guid, Task>? _deleteLocalData;

    public AccountWindow() => InitializeComponent();

    public AccountWindow(AccountViewModel viewModel,
        Func<Guid, Task> deleteLocalData) : this()
    {
        DataContext = viewModel;
        _deleteLocalData = deleteLocalData;
    }

    private AccountViewModel ViewModel => (AccountViewModel)DataContext!;

    private async void ChangePassword_OnClick(object? sender,
        RoutedEventArgs eventArgs) => await ViewModel.ChangePasswordAsync();

    private async void DeleteAccount_OnClick(object? sender,
        RoutedEventArgs eventArgs)
    {
        if (!await ViewModel.DeleteAccountAsync()) return;
        bool deleteLocal = await ConfirmLocalDataDeletionAsync();
        if (deleteLocal && _deleteLocalData is not null)
        {
            try { await _deleteLocalData(ViewModel.UserId); }
            catch (Exception exception)
            {
                ViewModel.SetExternalError(
                    $"서버 계정은 삭제됐지만 로컬 데이터 삭제에 실패했습니다: {exception.Message}");
                return;
            }
        }
        Close();
    }

    private async Task<bool> ConfirmLocalDataDeletionAsync()
    {
        bool deleteLocal = false;
        var keep = new Button
        {
            Content = "이 PC 데이터 유지", MinWidth = 140, IsCancel = true
        };
        var delete = new Button
        {
            Content = "로컬 데이터 삭제", MinWidth = 140, IsDefault = true
        };
        delete.Classes.Add("danger");
        var dialog = new Window
        {
            Title = "로컬 데이터 선택",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "이 PC의 계정 데이터를 삭제할까요?",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "유지하면 서버 계정은 삭제된 상태로 로컬 일정만 이 PC에 남습니다. 삭제하면 이 계정의 로컬 일정도 복구할 수 없습니다.",
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 420
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { keep, delete }
                    }
                }
            }
        };
        dialog.Classes.Add("dialog");
        keep.Click += (_, _) => dialog.Close();
        delete.Click += (_, _) =>
        {
            deleteLocal = true;
            dialog.Close();
        };
        await dialog.ShowDialog(this);
        return deleteLocal;
    }
}
