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

public enum CalendarEditMode
{
    Single,
    Range,
    Recurring
}

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
    private SeriesEditorState _originalSeriesState;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private TimeSpan? _timeValue;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private string _color = DefaultTextColor;
    [ObservableProperty] private bool _isAnniversary;
    [ObservableProperty] private CalendarEditMode _mode;
    [ObservableProperty] private DateTimeOffset? _endDateValue;
    [ObservableProperty] private int _recurrenceFrequencyIndex;
    [ObservableProperty] private decimal _recurrenceInterval = 1;
    [ObservableProperty] private bool _sunday;
    [ObservableProperty] private bool _monday;
    [ObservableProperty] private bool _tuesday;
    [ObservableProperty] private bool _wednesday;
    [ObservableProperty] private bool _thursday;
    [ObservableProperty] private bool _friday;
    [ObservableProperty] private bool _saturday;
    [ObservableProperty] private bool _hasRecurrenceUntil;
    [ObservableProperty] private DateTimeOffset? _recurrenceUntilValue;

    public Guid? SourceId => _source?.Id;
    public bool IsEditing => _source is not null;
    public string FormTitle => IsEditing
        ? IsAnniversary ? "기념일 수정" : "일정 수정"
        : IsAnniversary ? "새 기념일" : "새 일정";
    public string SaveButtonText => IsEditing ? "변경 저장" :
        IsAnniversary ? "기념일 추가" : "일정 추가";
    public bool IsSchedule => !IsAnniversary;
    public bool IsSingleMode => IsSchedule && Mode == CalendarEditMode.Single;
    public bool IsRangeMode => IsSchedule && Mode == CalendarEditMode.Range;
    public bool IsRecurringMode => IsSchedule &&
        Mode == CalendarEditMode.Recurring;
    public bool IsWeeklyRecurrence => IsRecurringMode &&
        RecurrenceFrequencyIndex == (int)RecurrenceFrequency.Weekly;
    public bool ShowsSeriesScopeNotice => IsEditing &&
        (IsAnniversary || Mode != CalendarEditMode.Single);
    public string StartDateText => $"{_date:yyyy년 M월 d일}";
    public string CompletionLabel => Mode == CalendarEditMode.Single
        ? "완료된 일정" : "전체 시리즈 완료";
    public string SeriesScopeText => IsAnniversary
        ? "기념일 전체 시리즈를 수정합니다. 매년 같은 날짜에 반영됩니다."
        : Mode == CalendarEditMode.Range
            ? "기간 전체를 하나의 일정으로 수정·완료·삭제합니다."
            : "모든 반복 발생 날짜에 적용되는 전체 시리즈를 수정합니다.";
    public string SeriesSummary => IsAnniversary ? "매년 · 종료 없음" :
        Mode switch
        {
            CalendarEditMode.Single => "하루 일정",
            CalendarEditMode.Range => EndDateValue is { } end
                ? $"{StartDateText}부터 {ToDate(end):yyyy년 M월 d일}까지"
                : "종료일을 선택하세요.",
            CalendarEditMode.Recurring => FormatRecurrenceSummary(),
            _ => string.Empty
        };
    public bool HasUnsavedChanges =>
        !string.Equals(Title, _originalTitle, StringComparison.Ordinal) ||
        TimeValue != _originalTime ||
        !string.Equals(Notes, _originalNotes, StringComparison.Ordinal) ||
        IsCompleted != _originalCompleted ||
        !string.Equals(Color, _originalColor,
            StringComparison.OrdinalIgnoreCase) ||
        CaptureSeriesState() != _originalSeriesState;
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
            if (IsRangeMode)
            {
                if (EndDateValue is null) return "종료일을 선택하세요.";
                if (ToDate(EndDateValue.Value) <= _date)
                    return "기간 종료일은 시작일보다 늦어야 합니다.";
            }
            if (IsRecurringMode)
            {
                if (RecurrenceInterval < 1 ||
                    RecurrenceInterval != decimal.Truncate(
                        RecurrenceInterval))
                    return "반복 간격은 1 이상의 정수여야 합니다.";
                if (RecurrenceFrequencyIndex < 0 ||
                    RecurrenceFrequencyIndex >
                        (int)RecurrenceFrequency.Yearly)
                    return "반복 빈도를 선택하세요.";
                if (IsWeeklyRecurrence && GetWeekdayMask() == 0)
                    return "매주 반복할 요일을 한 개 이상 선택하세요.";
                if (HasRecurrenceUntil)
                {
                    if (RecurrenceUntilValue is null)
                        return "반복 종료일을 선택하세요.";
                    if (ToDate(RecurrenceUntilValue.Value) < _date)
                        return "반복 종료일은 시작일보다 빠를 수 없습니다.";
                }
            }
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
            DefaultTextColor, false, null);
    }

    public void BeginEdit(CalendarItem source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = Clone(source);
        _date = source.StartDate;
        LoadValues(source.Title, source.StartTime?.ToTimeSpan(), source.Notes,
            source.IsCompleted, source.Color, source.IsAnniversary, source);
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
        item.Kind = IsAnniversary ? CalendarItemKind.Anniversary :
            CalendarItemKind.Schedule;
        item.IsCompleted = IsAnniversary ? false : IsCompleted;
        if (IsAnniversary)
        {
            item.EndDate = item.StartDate;
            item.Recurrence = new RecurrenceRule(RecurrenceFrequency.Yearly);
        }
        else if (Mode == CalendarEditMode.Range)
        {
            item.EndDate = ToDate(EndDateValue!.Value);
            item.Recurrence = null;
        }
        else if (Mode == CalendarEditMode.Recurring)
        {
            item.EndDate = item.StartDate;
            item.Recurrence = new RecurrenceRule(
                (RecurrenceFrequency)RecurrenceFrequencyIndex,
                decimal.ToInt32(RecurrenceInterval),
                IsWeeklyRecurrence ? GetSelectedWeekdays() : null,
                HasRecurrenceUntil
                    ? ToDate(RecurrenceUntilValue!.Value) : null);
        }
        else
        {
            item.EndDate = item.StartDate;
            item.Recurrence = null;
        }
        item.Color = color.NormalizedColor;
        return item;
    }

    partial void OnTitleChanged(string value) => NotifyStateChanged();
    partial void OnTimeValueChanged(TimeSpan? value) => NotifyStateChanged();
    partial void OnNotesChanged(string value) => NotifyStateChanged();
    partial void OnIsCompletedChanged(bool value) => NotifyStateChanged();
    partial void OnColorChanged(string value) => NotifyStateChanged();
    partial void OnIsAnniversaryChanged(bool value)
    {
        if (_loading) return;
        _loading = true;
        if (value)
        {
            IsCompleted = false;
            Mode = CalendarEditMode.Recurring;
            EndDateValue = null;
            RecurrenceFrequencyIndex = (int)RecurrenceFrequency.Yearly;
            RecurrenceInterval = 1;
            SetWeekdays([]);
            HasRecurrenceUntil = false;
            RecurrenceUntilValue = null;
        }
        else
        {
            Mode = CalendarEditMode.Single;
            EndDateValue = null;
            ResetRecurrenceFields();
        }
        _loading = false;
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(IsSchedule));
        NotifySeriesStateChanged();
    }

    partial void OnModeChanged(CalendarEditMode value)
    {
        if (_loading) return;
        _loading = true;
        if (value == CalendarEditMode.Range)
            EndDateValue = ToDateTimeOffset(_date.AddDays(1));
        else
            EndDateValue = null;
        ResetRecurrenceFields();
        _loading = false;
        NotifySeriesStateChanged();
    }

    partial void OnEndDateValueChanged(DateTimeOffset? value) =>
        NotifySeriesStateChanged();

    partial void OnRecurrenceFrequencyIndexChanged(int value)
    {
        if (_loading) return;
        _loading = true;
        SetWeekdays(value == (int)RecurrenceFrequency.Weekly
            ? [_date.DayOfWeek] : []);
        _loading = false;
        NotifySeriesStateChanged();
    }

    partial void OnRecurrenceIntervalChanged(decimal value) =>
        NotifySeriesStateChanged();
    partial void OnSundayChanged(bool value) => NotifySeriesStateChanged();
    partial void OnMondayChanged(bool value) => NotifySeriesStateChanged();
    partial void OnTuesdayChanged(bool value) => NotifySeriesStateChanged();
    partial void OnWednesdayChanged(bool value) => NotifySeriesStateChanged();
    partial void OnThursdayChanged(bool value) => NotifySeriesStateChanged();
    partial void OnFridayChanged(bool value) => NotifySeriesStateChanged();
    partial void OnSaturdayChanged(bool value) => NotifySeriesStateChanged();

    partial void OnHasRecurrenceUntilChanged(bool value)
    {
        if (_loading) return;
        _loading = true;
        RecurrenceUntilValue = value
            ? ToDateTimeOffset(_date.AddYears(1)) : null;
        _loading = false;
        NotifySeriesStateChanged();
    }

    partial void OnRecurrenceUntilValueChanged(DateTimeOffset? value) =>
        NotifySeriesStateChanged();

    public void SetMode(CalendarEditMode mode) => Mode = mode;

    private void LoadValues(string title, TimeSpan? time, string notes,
        bool completed, string color, bool anniversary,
        CalendarItem? seriesSource)
    {
        _loading = true;
        Title = _originalTitle = title;
        TimeValue = _originalTime = time;
        Notes = _originalNotes = notes;
        IsCompleted = _originalCompleted = completed;
        Color = _originalColor = color;
        IsAnniversary = anniversary;
        RecurrenceRule? recurrence = seriesSource?.Recurrence;
        Mode = anniversary || recurrence is not null
            ? CalendarEditMode.Recurring
            : seriesSource is { } source && source.EndDate > source.StartDate
                ? CalendarEditMode.Range : CalendarEditMode.Single;
        EndDateValue = Mode == CalendarEditMode.Range && seriesSource is not null
            ? ToDateTimeOffset(seriesSource.EndDate) : null;
        RecurrenceFrequencyIndex = recurrence is null
            ? (int)RecurrenceFrequency.Daily : (int)recurrence.Frequency;
        RecurrenceInterval = recurrence?.Interval ?? 1;
        SetWeekdays(recurrence?.DaysOfWeek ?? []);
        HasRecurrenceUntil = recurrence?.Until is not null;
        RecurrenceUntilValue = recurrence?.Until is { } until
            ? ToDateTimeOffset(until) : null;
        _loading = false;
        _originalSeriesState = CaptureSeriesState();
        NotifyStateChanged();
        OnPropertyChanged(nameof(SourceId));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        OnPropertyChanged(nameof(IsSchedule));
        OnPropertyChanged(nameof(StartDateText));
        NotifySeriesStateChanged();
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

    private void NotifySeriesStateChanged()
    {
        if (_loading) return;
        NotifyStateChanged();
        OnPropertyChanged(nameof(IsSingleMode));
        OnPropertyChanged(nameof(IsRangeMode));
        OnPropertyChanged(nameof(IsRecurringMode));
        OnPropertyChanged(nameof(IsWeeklyRecurrence));
        OnPropertyChanged(nameof(ShowsSeriesScopeNotice));
        OnPropertyChanged(nameof(CompletionLabel));
        OnPropertyChanged(nameof(SeriesScopeText));
        OnPropertyChanged(nameof(SeriesSummary));
    }

    private void ResetRecurrenceFields()
    {
        RecurrenceFrequencyIndex = (int)RecurrenceFrequency.Daily;
        RecurrenceInterval = 1;
        SetWeekdays([]);
        HasRecurrenceUntil = false;
        RecurrenceUntilValue = null;
    }

    private void SetWeekdays(IEnumerable<DayOfWeek> values)
    {
        HashSet<DayOfWeek> selected = values.ToHashSet();
        Sunday = selected.Contains(DayOfWeek.Sunday);
        Monday = selected.Contains(DayOfWeek.Monday);
        Tuesday = selected.Contains(DayOfWeek.Tuesday);
        Wednesday = selected.Contains(DayOfWeek.Wednesday);
        Thursday = selected.Contains(DayOfWeek.Thursday);
        Friday = selected.Contains(DayOfWeek.Friday);
        Saturday = selected.Contains(DayOfWeek.Saturday);
    }

    private IReadOnlyList<DayOfWeek> GetSelectedWeekdays()
    {
        var days = new List<DayOfWeek>(7);
        if (Sunday) days.Add(DayOfWeek.Sunday);
        if (Monday) days.Add(DayOfWeek.Monday);
        if (Tuesday) days.Add(DayOfWeek.Tuesday);
        if (Wednesday) days.Add(DayOfWeek.Wednesday);
        if (Thursday) days.Add(DayOfWeek.Thursday);
        if (Friday) days.Add(DayOfWeek.Friday);
        if (Saturday) days.Add(DayOfWeek.Saturday);
        return days;
    }

    private int GetWeekdayMask()
    {
        int mask = 0;
        foreach (DayOfWeek day in GetSelectedWeekdays())
            mask |= 1 << (int)day;
        return mask;
    }

    private string FormatRecurrenceSummary()
    {
        int interval = RecurrenceInterval >= 1 &&
            RecurrenceInterval == decimal.Truncate(RecurrenceInterval)
                ? decimal.ToInt32(RecurrenceInterval) : 0;
        string cadence = RecurrenceFrequencyIndex switch
        {
            (int)RecurrenceFrequency.Daily => interval == 1
                ? "매일" : $"{interval}일마다",
            (int)RecurrenceFrequency.Weekly => interval == 1
                ? "매주" : $"{interval}주마다",
            (int)RecurrenceFrequency.Monthly => interval == 1
                ? "매월" : $"{interval}개월마다",
            (int)RecurrenceFrequency.Yearly => interval == 1
                ? "매년" : $"{interval}년마다",
            _ => "반복 빈도 미선택"
        };
        if (IsWeeklyRecurrence)
        {
            string weekdays = string.Join("·", GetSelectedWeekdays().Select(
                day => day switch
                {
                    DayOfWeek.Sunday => "일",
                    DayOfWeek.Monday => "월",
                    DayOfWeek.Tuesday => "화",
                    DayOfWeek.Wednesday => "수",
                    DayOfWeek.Thursday => "목",
                    DayOfWeek.Friday => "금",
                    DayOfWeek.Saturday => "토",
                    _ => string.Empty
                }));
            cadence += weekdays.Length > 0 ? $" · {weekdays}" : " · 요일 미선택";
        }
        string end = HasRecurrenceUntil && RecurrenceUntilValue is { } until
            ? $" · {ToDate(until):yyyy년 M월 d일}까지" : " · 종료 없음";
        return cadence + end;
    }

    private SeriesEditorState CaptureSeriesState() => new(IsAnniversary, Mode,
        EndDateValue is { } end ? ToDate(end) : null,
        RecurrenceFrequencyIndex, RecurrenceInterval, GetWeekdayMask(),
        HasRecurrenceUntil,
        RecurrenceUntilValue is { } until ? ToDate(until) : null);

    private static DateOnly ToDate(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.Date);

    private static DateTimeOffset ToDateTimeOffset(DateOnly value) => new(
        value.Year, value.Month, value.Day, 0, 0, 0, TimeSpan.Zero);

    private readonly record struct SeriesEditorState(bool IsAnniversary,
        CalendarEditMode Mode, DateOnly? EndDate, int FrequencyIndex,
        decimal Interval, int WeekdayMask, bool HasUntil, DateOnly? Until);

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

public sealed partial class CalendarDateBackgroundViewModel : ObservableObject
{
    private DateCellDecoration? _source;
    private DateOnly _date;
    private bool _loading;
    private string _originalColor = string.Empty;

    [ObservableProperty] private string _color = string.Empty;

    public Guid? SourceId => _source?.Id;
    public bool HasBackground => !string.IsNullOrWhiteSpace(Color);
    public bool HasUnsavedChanges => !string.Equals(Color, _originalColor,
        StringComparison.OrdinalIgnoreCase);
    public CellColorValidation ColorValidation => HasBackground
        ? CalendarCellColor.Validate(Color)
        : new CellColorValidation(true, string.Empty, string.Empty);
    public string ValidationMessage => ColorValidation.Message;
    public bool HasValidationError => !ColorValidation.IsValid;
    public bool CanSave => HasUnsavedChanges && !HasValidationError;
    public IBrush PreviewBackgroundBrush => HasBackground &&
        ColorValidation.IsValid
            ? new SolidColorBrush(Avalonia.Media.Color.Parse(
                ColorValidation.NormalizedColor))
            : Brushes.White;
    public IBrush PreviewForegroundBrush => HasBackground &&
        ColorValidation.IsValid
            ? new SolidColorBrush(Avalonia.Media.Color.Parse(
                CalendarCellColor.GetForeground(
                    ColorValidation.NormalizedColor)))
            : Brushes.Black;

    public void Begin(DateOnly date, DateCellDecoration? source)
    {
        _date = date;
        _source = source is null ? null : Clone(source);
        _loading = true;
        Color = _originalColor = source?.Color ?? string.Empty;
        _loading = false;
        NotifyStateChanged();
        OnPropertyChanged(nameof(SourceId));
    }

    public void SetPaletteColor(string color) => Color = color;
    public void Clear() => Color = string.Empty;

    public DateCellDecoration CreateDecoration()
    {
        if (!HasBackground || !ColorValidation.IsValid)
            throw new ArgumentException(HasBackground
                ? ValidationMessage : "배경색을 입력하세요.");
        return new DateCellDecoration
        {
            Id = _source?.Id ?? CalendarCellColor.GetDecorationId(_date),
            Date = _date,
            Kind = DateCellDecorationKind.Highlight,
            Color = ColorValidation.NormalizedColor,
            Label = string.Empty
        };
    }

    public void Accept(DateCellDecoration? source) => Begin(_date, source);

    partial void OnColorChanged(string value) => NotifyStateChanged();

    private void NotifyStateChanged()
    {
        if (_loading) return;
        OnPropertyChanged(nameof(HasBackground));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(ColorValidation));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(PreviewBackgroundBrush));
        OnPropertyChanged(nameof(PreviewForegroundBrush));
    }

    private static DateCellDecoration Clone(DateCellDecoration item) => new()
    {
        Id = item.Id,
        Date = item.Date,
        Kind = item.Kind,
        Color = item.Color,
        Label = item.Label,
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
    public string DateTitle => $"{Date:yyyy년 M월 d일} 일정과 기념일";
    public ObservableCollection<CalendarItem> Items { get; } = [];
    public CalendarEditorDraftViewModel Draft { get; } = new();
    public CalendarDateBackgroundViewModel Background { get; } = new();
    public bool HasUnsavedChanges => Draft.HasUnsavedChanges ||
        Background.HasUnsavedChanges;

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
        Background.Begin(date, null);
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
        if (HasUnsavedChanges && !discardChanges) return false;
        long request = Interlocked.Increment(ref _loadRequest);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _dispose.Token);
        await _loadGate.WaitAsync(linked.Token);
        try
        {
            Task<IReadOnlyList<CalendarOccurrence>> occurrenceTask = _repository
                .GetOccurrencesByRangeAsync(date, date, linked.Token);
            Task<IReadOnlyList<DateCellDecoration>> decorationTask = _repository
                .GetDecorationsByRangeAsync(date, date, linked.Token);
            await Task.WhenAll(occurrenceTask, decorationTask);
            IReadOnlyList<CalendarOccurrence> occurrences = await occurrenceTask;
            DateCellDecoration? background = (await decorationTask)
                .Where(item => item.Kind == DateCellDecorationKind.Highlight)
                .OrderByDescending(item => item.UpdatedAt)
                .FirstOrDefault();
            CalendarItem[] items = occurrences
                .GroupBy(occurrence => occurrence.SeriesId)
                .Select(group => group.First().Item)
                .OrderByDescending(item => item.IsAnniversary)
                .ThenBy(item => item.IsCompleted)
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
                Background.Begin(date, background);
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

    public Task<bool> SaveBackgroundAsync() => ExecuteAsync(async () =>
    {
        if (!Background.HasBackground)
        {
            if (Background.SourceId is { } id)
                await _repository.DeleteDecorationAsync(id, _dispose.Token);
            Background.Accept(null);
            return;
        }
        DateCellDecoration decoration = Background.CreateDecoration();
        await _repository.UpsertDecorationAsync(decoration, _dispose.Token);
        Background.Accept(decoration);
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
        if (_disposed || HasUnsavedChanges ||
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
