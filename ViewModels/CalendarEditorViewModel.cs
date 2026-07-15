using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HmDesktopCalendar.Calendar;

namespace HmDesktopCalendar.ViewModels;

public sealed partial class CalendarEditorDraftViewModel : ObservableObject
{
    public const string DefaultTextColor = CalendarTextColor.DefaultColor;
    private CalendarItem? _source;
    private DateOnly _date;
    private bool _loading;
    private string _originalTitle = string.Empty;
    private TimeSpan? _originalTime;
    private string _originalNotes = string.Empty;
    private bool _originalCompleted;
    private string _originalColor = DefaultTextColor;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private TimeSpan? _timeValue;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private string _color = DefaultTextColor;

    public Guid? SourceId => _source?.Id;
    public bool IsEditing => _source is not null;
    public string FormTitle => IsEditing ? "일정 수정" : "새 일정";
    public string SaveButtonText => IsEditing ? "변경 저장" : "일정 추가";
    public bool HasUnsavedChanges =>
        !string.Equals(Title, _originalTitle, StringComparison.Ordinal) ||
        TimeValue != _originalTime ||
        !string.Equals(Notes, _originalNotes, StringComparison.Ordinal) ||
        IsCompleted != _originalCompleted ||
        !string.Equals(Color, _originalColor, StringComparison.OrdinalIgnoreCase);
    public TextColorValidation ColorValidation =>
        CalendarTextColor.Validate(Color);
    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title)) return "제목을 입력하세요.";
            if (Title.Trim().Length > 500)
                return "제목은 500자 이하여야 합니다.";
            if (Notes.Length > 10000)
                return "메모는 10,000자 이하여야 합니다.";
            return ColorValidation.Message;
        }
    }
    public bool HasValidationError => ValidationMessage.Length > 0;
    public bool CanSave => HasUnsavedChanges && !HasValidationError;
    public string PreviewTitle => string.IsNullOrWhiteSpace(Title)
        ? "일정 제목 미리보기" : Title.Trim();
    public string PreviewTime => TimeValue is { } time
        ? $"{time:hh\\:mm}" : "시간 없음";
    public IBrush PreviewBrush
    {
        get
        {
            TextColorValidation validation = ColorValidation;
            return validation.IsValid
                ? new SolidColorBrush(Avalonia.Media.Color.Parse(
                    validation.NormalizedColor))
                : Brushes.Black;
        }
    }
    public string ContrastText => ColorValidation.IsValid
        ? $"대비 {ColorValidation.ContrastRatio:0.00}:1 · WCAG AA 충족"
        : "중립 일정 칩에서 4.5:1 이상의 대비가 필요합니다.";

    public void BeginNew(DateOnly date)
    {
        _source = null;
        _date = date;
        LoadValues(string.Empty, null, string.Empty, false,
            DefaultTextColor);
    }

    public void BeginEdit(CalendarItem source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = Clone(source);
        _date = source.StartDate;
        LoadValues(source.Title, source.StartTime?.ToTimeSpan(), source.Notes,
            source.IsCompleted, source.Color);
    }

    public void SetPaletteColor(string color) => Color = color;

    public CalendarItem CreateItem()
    {
        if (ValidationMessage.Length > 0)
            throw new ArgumentException(ValidationMessage);
        TextColorValidation color = ColorValidation;
        CalendarItem item = _source is null ? new CalendarItem
        {
            Kind = CalendarItemKind.Schedule,
            StartDate = _date,
            EndDate = _date
        } : Clone(_source);
        item.Title = Title.Trim();
        item.Notes = Notes.Trim();
        item.StartTime = TimeValue is { } time
            ? TimeOnly.FromTimeSpan(time) : null;
        item.IsAllDay = item.StartTime is null;
        item.IsCompleted = IsCompleted;
        item.Color = color.NormalizedColor;
        return item;
    }

    partial void OnTitleChanged(string value) => NotifyStateChanged();
    partial void OnTimeValueChanged(TimeSpan? value) => NotifyStateChanged();
    partial void OnNotesChanged(string value) => NotifyStateChanged();
    partial void OnIsCompletedChanged(bool value) => NotifyStateChanged();
    partial void OnColorChanged(string value) => NotifyStateChanged();

    private void LoadValues(string title, TimeSpan? time, string notes,
        bool completed, string color)
    {
        _loading = true;
        Title = _originalTitle = title;
        TimeValue = _originalTime = time;
        Notes = _originalNotes = notes;
        IsCompleted = _originalCompleted = completed;
        Color = _originalColor = color;
        _loading = false;
        NotifyStateChanged();
        OnPropertyChanged(nameof(SourceId));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SaveButtonText));
    }

    private void NotifyStateChanged()
    {
        if (_loading) return;
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ColorValidation));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(PreviewTitle));
        OnPropertyChanged(nameof(PreviewTime));
        OnPropertyChanged(nameof(PreviewBrush));
        OnPropertyChanged(nameof(ContrastText));
    }

    private static CalendarItem Clone(CalendarItem item) => new()
    {
        Id = item.Id,
        Kind = item.Kind,
        Title = item.Title,
        Notes = item.Notes,
        StartDate = item.StartDate,
        EndDate = item.EndDate,
        StartTime = item.StartTime,
        EndTime = item.EndTime,
        IsAllDay = item.IsAllDay,
        IsCompleted = item.IsCompleted,
        Color = item.Color,
        Recurrence = item.Recurrence is null ? null : new RecurrenceRule(
            item.Recurrence.Frequency, item.Recurrence.Interval,
            item.Recurrence.DaysOfWeek?.ToArray(), item.Recurrence.Until,
            item.Recurrence.Count),
        Reminders = item.Reminders.ToList(),
        IsDeleted = item.IsDeleted,
        Revision = item.Revision,
        Cursor = item.Cursor,
        UpdatedAt = item.UpdatedAt
    };
}

public sealed partial class CalendarEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ICalendarRepository _repository;
    private readonly Func<Action, Task> _updateUi;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly CancellationTokenSource _dispose = new();
    private long _loadRequest;
    private int _localOperation;
    private bool _disposed;

    [ObservableProperty] private DateOnly _date;
    [ObservableProperty] private string _errorMessage = string.Empty;
    public string DateTitle => $"{Date:yyyy년 M월 d일} 일정";
    public ObservableCollection<CalendarItem> Items { get; } = [];
    public CalendarEditorDraftViewModel Draft { get; } = new();

    public CalendarEditorViewModel(DateOnly date, ICalendarRepository repository)
        : this(date, repository, UpdateOnUiThreadAsync)
    {
    }

    public CalendarEditorViewModel(DateOnly date, ICalendarRepository repository,
        Func<Action, Task> updateUi)
    {
        _date = date;
        _repository = repository;
        _updateUi = updateUi;
        Draft.BeginNew(date);
        _repository.Changed += OnRepositoryChanged;
    }

    partial void OnDateChanged(DateOnly value) =>
        OnPropertyChanged(nameof(DateTitle));

    public Task<bool> LoadAsync() => LoadDateAsync(Date);

    public async Task<bool> LoadDateAsync(DateOnly date,
        bool discardChanges = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Draft.HasUnsavedChanges && !discardChanges) return false;
        long request = Interlocked.Increment(ref _loadRequest);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _dispose.Token);
        await _loadGate.WaitAsync(linked.Token);
        try
        {
            IReadOnlyList<CalendarOccurrence> occurrences = await _repository
                .GetOccurrencesByRangeAsync(date, date, linked.Token);
            CalendarItem[] items = occurrences
                .Where(occurrence =>
                    occurrence.Item.Kind == CalendarItemKind.Schedule)
                .GroupBy(occurrence => occurrence.SeriesId)
                .Select(group => group.First().Item)
                .OrderBy(item => item.IsCompleted)
                .ThenBy(item => item.StartTime)
                .ThenBy(item => item.Title, StringComparer.CurrentCulture)
                .ToArray();
            if (request != Volatile.Read(ref _loadRequest)) return false;
            await _updateUi(() =>
            {
                if (request != Volatile.Read(ref _loadRequest)) return;
                Date = date;
                Items.Clear();
                foreach (CalendarItem item in items) Items.Add(item);
                Draft.BeginNew(date);
                ErrorMessage = string.Empty;
            });
            return true;
        }
        finally { _loadGate.Release(); }
    }

    public bool BeginNew(bool discardChanges = false)
    {
        if (Draft.HasUnsavedChanges && !discardChanges) return false;
        Draft.BeginNew(Date);
        ErrorMessage = string.Empty;
        return true;
    }

    public bool BeginEdit(CalendarItem item, bool discardChanges = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Draft.HasUnsavedChanges && !discardChanges)
            return Draft.SourceId == item.Id;
        Draft.BeginEdit(item);
        ErrorMessage = string.Empty;
        return true;
    }

    public void CancelDraft()
    {
        Draft.BeginNew(Date);
        ErrorMessage = string.Empty;
    }

    public Task<bool> SaveDraftAsync() => ExecuteAsync(async () =>
    {
        CalendarItem item = Draft.CreateItem();
        await _repository.UpsertItemAsync(item, _dispose.Token);
        await LoadDateAsync(Date, true, _dispose.Token);
    });

    public Task<bool> DeleteAsync(CalendarItem item) => ExecuteAsync(async () =>
    {
        await _repository.DeleteItemAsync(item.Id, _dispose.Token);
        await LoadDateAsync(Date, true, _dispose.Token);
    });

    private async Task<bool> ExecuteAsync(Func<Task> action)
    {
        Interlocked.Increment(ref _localOperation);
        try
        {
            await action();
            ErrorMessage = string.Empty;
            return true;
        }
        catch (OperationCanceledException) when (_dispose.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"저장하지 못했습니다: {exception.Message}";
            return false;
        }
        finally { Interlocked.Decrement(ref _localOperation); }
    }

    private void OnRepositoryChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed || Draft.HasUnsavedChanges ||
            Volatile.Read(ref _localOperation) != 0) return;
        _ = ReloadSafelyAsync();
    }

    private async Task ReloadSafelyAsync()
    {
        try { await LoadDateAsync(Date, false, _dispose.Token); }
        catch (OperationCanceledException) when (_dispose.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_disposed) { }
        catch (Exception exception)
        {
            await _updateUi(() => ErrorMessage =
                $"일정을 불러오지 못했습니다: {exception.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
        _dispose.Cancel();
        _dispose.Dispose();
    }

    private static async Task UpdateOnUiThreadAsync(Action update) =>
        await Dispatcher.UIThread.InvokeAsync(update);
}
