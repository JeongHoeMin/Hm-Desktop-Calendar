using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HmDesktopCalendar.Authentication;

namespace HmDesktopCalendar.Views;

public partial class LoginWindow : Window
{
    private readonly AuthSession _session = null!;
    public event EventHandler? Authenticated;
    public LoginWindow() => InitializeComponent();
    public LoginWindow(AuthSession session) : this() => _session = session;
    private async void Login_OnClick(object? s,RoutedEventArgs e)=>await RunAsync(()=>_session.LoginAsync(EmailBox.Text??"",PasswordBox.Text??""));
    private async void Register_OnClick(object? s,RoutedEventArgs e)=>await RunAsync(()=>_session.RegisterAsync(EmailBox.Text??"",PasswordBox.Text??""));
    private async Task RunAsync(Func<Task> action)
    {
        try { IsEnabled=false; ErrorText.Text=""; await action(); Authenticated?.Invoke(this,EventArgs.Empty); Close(); }
        catch(Exception ex){ErrorText.Text=ex.Message;} finally{IsEnabled=true;}
    }
}
