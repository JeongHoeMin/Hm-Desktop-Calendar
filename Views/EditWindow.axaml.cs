using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HmDesktopCalendar.Todos;
using HmDesktopCalendar.ViewModels;

namespace HmDesktopCalendar.Views;

public partial class EditWindow : Window
{
    private TodoEditorViewModel ViewModel => (TodoEditorViewModel)DataContext!;
    public EditWindow() => InitializeComponent();
    public EditWindow(TodoEditorViewModel vm) : this()
    {
        DataContext = vm;
        Opened += async (_, _) =>
        {
            try { await vm.LoadAsync(); }
            catch (Exception exception)
            {
                vm.ErrorMessage = $"할 일을 불러오지 못했습니다: {exception.Message}";
            }
        };
        Closed += (_, _) => vm.Dispose();
    }
    public Task ShowDateAsync(DateOnly date) => ViewModel.LoadDateAsync(date);
    private async void Add_OnClick(object? s, RoutedEventArgs e)
    {
        TimeOnly? time = NewTime.SelectedTime is { } selected ? TimeOnly.FromTimeSpan(selected) : null;
        if (await ViewModel.AddAsync(NewTitle.Text ?? "", time, ""))
        { NewTitle.Clear(); NewTime.SelectedTime = null; }
    }
    private void PresetTime_OnClick(object? sender, RoutedEventArgs e)
    {
        string? value = (sender as Button)?.Tag as string;
        NewTime.SelectedTime = value switch { "none" => null, "now" => DateTime.Now.TimeOfDay,
            _ when TimeSpan.TryParse(value, out var time) => time, _ => NewTime.SelectedTime };
    }
    private async void Save_OnClick(object? s, RoutedEventArgs e) => await SaveFromAsync(s);
    private async void Complete_OnClick(object? s, RoutedEventArgs e) => await SaveFromAsync(s);
    private async Task SaveFromAsync(object? sender)
    {
        if ((sender as Control)?.DataContext is TodoItem item)
            await ViewModel.SaveAsync(item);
    }
    private async void Delete_OnClick(object? s, RoutedEventArgs e)
    {
        if ((s as Control)?.DataContext is TodoItem item)
            await ViewModel.DeleteAsync(item);
    }
}
