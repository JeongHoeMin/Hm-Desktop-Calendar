using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HmDesktopCalendar.Authentication;
using HmDesktopCalendar.Calendar;
using HmDesktopCalendar.DesktopIntegration;
using HmDesktopCalendar.Reminders;
using HmDesktopCalendar.Services;
using HmDesktopCalendar.Todos;
using HmDesktopCalendar.ViewModels;

namespace HmDesktopCalendar.RegressionTests;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("바탕화면 클릭 대상만 허용한다", DesktopSurfaceTargetsOnly),
            ("서로 다른 창의 클릭은 더블클릭이 아니다", DoubleClickRequiresSameTarget),
            ("Bounds 적용은 Z 순서를 보존한다", BoundsDoesNotChangeLayer),
            ("레이어 적용은 Bounds를 보존한다", LayerDoesNotChangeBounds),
            ("수정 모드는 영구 Topmost를 사용하지 않는다", ForegroundEditingIsNotTopmost),
            ("저장 전환은 두 불변식을 함께 만족한다", CommitPreservesBothInvariants),
            ("레이어 실패 시 저장 모드를 완료하지 않는다", FailureRollsBackToEditing),
            ("상태 점검은 손상된 책임만 복구한다", MaintenanceRepairsOnlyBrokenInvariant),
            ("취소는 원래 Bounds와 레이어를 함께 복원한다", CancelRestoresOriginalBounds),
            ("Explorer 재시작은 새 호스트에 정확히 재부착한다", ExplorerRestartReattaches),
            ("화면과 겹치는 음수 좌표는 복구하지 않는다", VisibleNegativeBoundsArePreserved),
            ("달력 ViewModel이 연도와 월을 독립적으로 이동한다", CalendarViewModelMovesPredictably),
            ("한국 공휴일은 음력과 현행 고정일을 계산한다", KoreanHolidaysMatchKnownDates),
            ("한국 대체공휴일은 충돌 뒤 첫 평일로 이동한다", KoreanSubstituteHolidaysAvoidCollisions),
            ("달력은 공휴일을 새 그리드와 재사용 그리드에 합성한다", CalendarViewModelComposesHolidays),
            ("주 시작 설정은 요일 헤더와 42일 그리드를 함께 정렬한다", CalendarGridTracksWeekStart),
            ("날짜 숫자 색은 배경·공휴일·주말 우선순위를 지킨다", CalendarDayForegroundPriority),
            ("앱 설정은 기존 창 위치 JSON을 호환한다", AppSettingsLoadLegacyBounds),
            ("앱 설정은 원자 저장과 변경 알림을 제공한다", AppSettingsRoundTripAndNotify),
            ("손상된 앱 설정은 기본값으로 복구한다", AppSettingsFallbackFromCorruption),
            ("설정 화면은 창 위치를 즉시 초기화하고 저장한다", SettingsResetWindowBounds),
            ("설정 메뉴는 세션 상태에 맞는 문구를 표시한다", SettingsMenuTracksSession),
            ("계정 화면은 비밀번호와 삭제 확인 입력을 검증한다", AccountInputsAreValidated),
            ("계정 화면은 인증 오류를 한국어로 매핑한다", AccountErrorsAreMapped),
            ("계정 작업 성공은 세션 종료 상태를 안내한다", AccountSuccessEndsSession),
            ("로컬 계정 삭제는 선택한 범위만 제거한다", AccountScopeDeletionIsIsolated),
            ("달력 표시 설정은 즉시 적용되고 다시 로드된다", CalendarDisplaySettingsPersist),
            ("외형 프리셋은 달력 크기 토큰과 불투명도를 계산한다", CalendarAppearanceCalculatesTokens),
            ("외형 설정은 즉시 적용되고 다시 로드된다", CalendarAppearanceSettingsPersist),
            ("자동 시작 등록은 테스트 레지스트리 값을 왕복한다", AutoStartRegistryRoundTrip),
            ("자동 시작 오류는 토글을 비활성화하고 사유를 표시한다", AutoStartFailureDisablesToggle),
            ("로컬 백업은 데이터 범위와 하루 한 번 규칙을 지킨다", BackupCopiesDataOncePerDay),
            ("로컬 백업은 최근 10개 세대만 보관한다", BackupRetainsTenGenerations),
            ("로컬 백업 실패는 부분 세대를 남기지 않는다", BackupFailureLeavesNoPartialGeneration),
            ("동기화 실패가 마지막 성공 시각을 보존한다", SynchronizationStatePreservesLastSuccess),
            ("기존 할 일을 v2 문서로 원자적으로 가져온다", LegacyTodosAreImportedAtomically),
            ("가져오기 실패 후 원본으로 복구할 수 있다", FailedImportCanBeRetried),
            ("로컬 캘린더 저장소는 계정 범위를 분리한다", CalendarAccountsAreIsolated),
            ("일정과 날짜 장식은 tombstone과 커서를 보존한다", CalendarCrudPreservesSyncData),
            ("단일·기간 일정은 조회 경계를 포함해 날짜를 만든다", PeriodOccurrencesRespectRange),
            ("일일 반복은 간격과 포함 종료일을 지킨다", DailyOccurrencesRespectIntervalAndUntil),
            ("주간 반복은 선택 요일과 주 간격을 지킨다", WeeklyOccurrencesRespectDaysAndInterval),
            ("월간 반복은 존재하지 않는 월말을 건너뛴다", MonthlyOccurrencesSkipMissingMonthDays),
            ("연간 2월 29일 반복은 윤년에만 발생한다", YearlyOccurrencesUseLeapYears),
            ("무기한 반복은 조회 범위 안에서만 계산한다", UnboundedOccurrencesStayBounded),
            ("비지원 반복 규칙을 명시적으로 거부한다", UnsupportedRecurrencesAreRejected),
            ("발생 일정 변경은 전체 시리즈 원본에 적용된다", OccurrenceChangesApplyToWholeSeries),
            ("발생 일정 조회는 결정론적으로 정렬된다", OccurrencesAreDeterministicallyOrdered),
            ("텍스트 색상은 형식과 WCAG 대비를 검증한다", TextColorsRequireAccessibleContrast),
            ("날짜 배경은 정규화하고 읽기 쉬운 전경을 선택한다", DateBackgroundColorsAreNormalized),
            ("이전 기본 색상은 접근 가능한 기본값으로 마이그레이션한다", LegacyTextColorIsMigrated),
            ("편집 초안은 변경을 감지하고 시리즈 필드를 보존한다", EditorDraftPreservesSeriesFields),
            ("기념일 편집은 완료 없는 연간 반복을 만든다", AnniversaryDraftCreatesYearlySeries),
            ("편집 모드 전환은 사용하지 않는 필드를 초기화한다", EditorModeTransitionsResetUnusedFields),
            ("기간·반복 입력은 잘못된 규칙을 차단한다", EditorSeriesValidationRejectsInvalidRules),
            ("지원 반복 규칙은 편집 후 손실 없이 복원된다", EditorRecurrencesRoundTrip),
            ("파생 날짜 편집은 전체 시리즈 원본을 변경한다", DerivedDateEditorUpdatesWholeSeries),
            ("날짜 배경 편집은 저장과 초기화를 같은 엔터티에 적용한다", DateBackgroundEditorConverges),
            ("저장하지 않은 편집은 일정·날짜 전환을 차단한다", EditorNavigationProtectsUnsavedChanges),
            ("추가·수정·완료·삭제는 중복 없이 저장된다", EditorOperationsDoNotDuplicateItems),
            ("달력 미리보기는 일정 텍스트 색상을 사용한다", CalendarPreviewUsesItemColor),
            ("달력은 날짜 배경과 기념일 배지를 함께 표시한다", CalendarPreviewShowsBackgroundAndAnniversary),
            ("서버 병합은 전송 전 로컬 변경을 보존한다", ServerMergePreservesPendingLocalChanges),
            ("v2 계정 범위는 익명 데이터만 최초 계정으로 이동한다", CalendarScopesStayIsolated),
            ("v2 원격 클라이언트는 통합 일정 계약을 사용한다", RemoteCalendarClientUsesV2Contract),
            ("알림 편집은 프리셋과 시간 없는 일정 기준 시각을 저장한다", ReminderEditorCreatesAnchors),
            ("알림 저장소는 기준 시각과 허용 범위를 검증한다", ReminderRepositoryValidatesRules),
            ("알림 스케줄러는 중복·다시 알림·기간 경계를 처리한다", ReminderSchedulerHandlesLifecycle),
            ("ICS는 일정과 기간을 표준 VEVENT로 직렬화한다", IcsSerializesEventsAndPeriods),
            ("ICS는 반복 규칙을 RRULE로 보존한다", IcsPreservesRecurrenceRules),
            ("ICS는 UTF-8 folding과 원자 저장을 지킨다", IcsFoldsUtf8AndWritesAtomically),
            ("ICS 내보내기는 화면 필터와 무관한 전체 원본을 사용한다", IcsExportUsesAllSeries),
            ("모아보기는 기간·검색·상태·정렬을 조합한다", ScheduleOverviewCombinesFilters),
            ("모아보기는 연속 저장소 변경을 디바운스한다", ScheduleOverviewDebouncesRepositoryChanges)
        };
        int failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine($"PASS: {test.Name}"); }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}\n{exception}");
            }
        }
        return failures == 0 ? 0 : 1;
    }

    private static void LegacyTodosAreImportedAtomically() =>
        WithTempDirectory(directory =>
        {
            var id = Guid.NewGuid();
            string legacyPath = Path.Combine(directory, "todos.json");
            File.WriteAllText(legacyPath, JsonSerializer.Serialize(new[]
            {
                new TodoItem
                {
                    Id = id,
                    Date = new DateOnly(2026, 7, 15),
                    Title = "기존 할 일",
                    Notes = "보존할 메모",
                    Time = new TimeOnly(9, 30),
                    IsCompleted = true,
                    Revision = 7,
                    Cursor = 11
                }
            }));

            using var repository = new LocalCalendarRepository(directory);
            IReadOnlyList<CalendarItem> items = repository
                .GetItemsByRangeAsync(new DateOnly(2026, 7, 1),
                    new DateOnly(2026, 7, 31)).GetAwaiter().GetResult();

            Assert(items.Count == 1 && items[0].Id == id,
                "기존 할 일을 가져오지 못했습니다.");
            Assert(items[0].Title == "기존 할 일" &&
                   items[0].Notes == "보존할 메모" &&
                   items[0].StartTime == new TimeOnly(9, 30) &&
                   items[0].IsCompleted,
                "기존 할 일 필드가 손실되었습니다.");
            Assert(items[0].Revision == 0 && items[0].Cursor == 0,
                "v1 커서를 v2 동기화 상태로 잘못 가져왔습니다.");
            Assert(File.Exists(legacyPath), "가져온 뒤 기존 파일이 삭제되었습니다.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(directory, "calendar-v2.json")));
            Assert(document.RootElement.GetProperty("Version").GetInt32() == 2,
                "v2 문서 버전이 기록되지 않았습니다.");
        });

    private static void FailedImportCanBeRetried() =>
        WithTempDirectory(directory =>
        {
            string legacyPath = Path.Combine(directory, "todos.json");
            File.WriteAllText(legacyPath, "{ broken json");
            using var repository = new LocalCalendarRepository(directory);
            bool failed = false;
            try
            {
                repository.GetItemsByRangeAsync(DateOnly.MinValue,
                    DateOnly.MaxValue).GetAwaiter().GetResult();
            }
            catch (JsonException) { failed = true; }
            Assert(failed, "손상된 가져오기 원본을 성공으로 처리했습니다.");
            Assert(!File.Exists(Path.Combine(directory, "calendar-v2.json")),
                "실패한 가져오기가 불완전한 v2 문서를 남겼습니다.");

            File.WriteAllText(legacyPath, "[]");
            IReadOnlyList<CalendarItem> recovered = repository
                .GetItemsByRangeAsync(DateOnly.MinValue, DateOnly.MaxValue)
                .GetAwaiter().GetResult();
            Assert(recovered.Count == 0 &&
                   File.Exists(Path.Combine(directory, "calendar-v2.json")),
                "원본 복구 후 가져오기를 재시도하지 못했습니다.");
        });

    private static void CalendarAccountsAreIsolated() =>
        WithTempDirectory(directory =>
        {
            string firstDirectory = Path.Combine(directory, "accounts", "first");
            string secondDirectory = Path.Combine(directory, "accounts", "second");
            using var first = new LocalCalendarRepository(firstDirectory);
            using var second = new LocalCalendarRepository(secondDirectory);
            first.UpsertItemAsync(new CalendarItem
            {
                Title = "첫 계정",
                StartDate = new DateOnly(2026, 7, 15),
                EndDate = new DateOnly(2026, 7, 15)
            }).GetAwaiter().GetResult();

            Assert(first.GetItemsByRangeAsync(DateOnly.MinValue,
                       DateOnly.MaxValue).GetAwaiter().GetResult().Count == 1,
                "첫 계정 데이터가 저장되지 않았습니다.");
            Assert(second.GetItemsByRangeAsync(DateOnly.MinValue,
                       DateOnly.MaxValue).GetAwaiter().GetResult().Count == 0,
                "다른 계정에 일정 데이터가 섞였습니다.");
        });

    private static void CalendarCrudPreservesSyncData() =>
        WithTempDirectory(directory =>
        {
            using var repository = new LocalCalendarRepository(directory);
            var item = new CalendarItem
            {
                Title = "반복 일정",
                StartDate = new DateOnly(2026, 7, 15),
                EndDate = new DateOnly(2026, 7, 15),
                Recurrence = new RecurrenceRule(RecurrenceFrequency.Weekly,
                    2, [DayOfWeek.Wednesday]),
                Reminders = [new CalendarReminder(30, new TimeOnly(9, 0))]
            };
            var decoration = new DateCellDecoration
            {
                Date = new DateOnly(2026, 7, 15),
                Kind = DateCellDecorationKind.ColorDot,
                Color = "#FF0000",
                Label = "휴일"
            };
            repository.UpsertItemAsync(item).GetAwaiter().GetResult();
            repository.UpsertDecorationAsync(decoration).GetAwaiter().GetResult();
            repository.SetSyncStateAsync(new SyncState(42,
                new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero)))
                .GetAwaiter().GetResult();
            Assert(repository.GetDecorationsByRangeAsync(DateOnly.MinValue,
                       DateOnly.MaxValue).GetAwaiter().GetResult().Count == 1,
                "날짜 장식을 조회하지 못했습니다.");
            Assert(repository.GetSyncStateAsync().GetAwaiter().GetResult()
                       .Cursor == 42,
                "통합 커서를 보존하지 못했습니다.");

            repository.DeleteItemAsync(item.Id).GetAwaiter().GetResult();
            repository.DeleteDecorationAsync(decoration.Id).GetAwaiter().GetResult();
            Assert(repository.GetItemsByRangeAsync(DateOnly.MinValue,
                       DateOnly.MaxValue).GetAwaiter().GetResult().Count == 0,
                "삭제된 일정이 일반 조회에 노출되었습니다.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(directory, "calendar-v2.json")));
            Assert(document.RootElement.GetProperty("Items")[0]
                       .GetProperty("IsDeleted").GetBoolean(),
                "일정 삭제 tombstone이 저장되지 않았습니다.");
            Assert(document.RootElement.GetProperty("Decorations")[0]
                       .GetProperty("IsDeleted").GetBoolean(),
                "날짜 장식 삭제 tombstone이 저장되지 않았습니다.");
        });

    private static void PeriodOccurrencesRespectRange()
    {
        DateOnly singleDate = new(2026, 7, 3);
        var single = new CalendarItem
        {
            Title = "하루 일정",
            StartDate = singleDate,
            EndDate = singleDate
        };
        var item = new CalendarItem
        {
            Title = "휴가",
            StartDate = new DateOnly(2026, 6, 29),
            EndDate = new DateOnly(2026, 7, 2)
        };

        IReadOnlyList<CalendarOccurrence> occurrences =
            CalendarOccurrenceEngine.GetOccurrences(item,
                new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3));

        AssertOccurrenceDates(CalendarOccurrenceEngine.GetOccurrences(single,
            new DateOnly(2026, 7, 1), singleDate), singleDate);
        AssertOccurrenceDates(occurrences,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2));
    }

    private static void DailyOccurrencesRespectIntervalAndUntil()
    {
        var item = new CalendarItem
        {
            Title = "격일 일정",
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 1),
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Daily, 2,
                Until: new DateOnly(2026, 7, 7))
        };

        IReadOnlyList<CalendarOccurrence> occurrences =
            CalendarOccurrenceEngine.GetOccurrences(item,
                new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 10));

        AssertOccurrenceDates(occurrences,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3),
            new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 7));
    }

    private static void WeeklyOccurrencesRespectDaysAndInterval()
    {
        var item = new CalendarItem
        {
            Title = "격주 일정",
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 1),
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Weekly, 2,
                [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday])
        };

        IReadOnlyList<CalendarOccurrence> occurrences =
            CalendarOccurrenceEngine.GetOccurrences(item,
                new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 17));

        AssertOccurrenceDates(occurrences,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3),
            new DateOnly(2026, 7, 13), new DateOnly(2026, 7, 15),
            new DateOnly(2026, 7, 17));
    }

    private static void MonthlyOccurrencesSkipMissingMonthDays()
    {
        var item = new CalendarItem
        {
            Title = "월말 일정",
            StartDate = new DateOnly(2024, 1, 31),
            EndDate = new DateOnly(2024, 1, 31),
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Monthly,
                Until: new DateOnly(2024, 5, 31))
        };

        IReadOnlyList<CalendarOccurrence> occurrences =
            CalendarOccurrenceEngine.GetOccurrences(item,
                new DateOnly(2024, 1, 1), new DateOnly(2024, 5, 31));

        AssertOccurrenceDates(occurrences,
            new DateOnly(2024, 1, 31), new DateOnly(2024, 3, 31),
            new DateOnly(2024, 5, 31));
    }

    private static void YearlyOccurrencesUseLeapYears()
    {
        var item = new CalendarItem
        {
            Title = "윤년 일정",
            StartDate = new DateOnly(2024, 2, 29),
            EndDate = new DateOnly(2024, 2, 29),
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Yearly,
                Until: new DateOnly(2033, 2, 28))
        };

        IReadOnlyList<CalendarOccurrence> occurrences =
            CalendarOccurrenceEngine.GetOccurrences(item,
                new DateOnly(2024, 1, 1), new DateOnly(2033, 12, 31));

        AssertOccurrenceDates(occurrences,
            new DateOnly(2024, 2, 29), new DateOnly(2028, 2, 29),
            new DateOnly(2032, 2, 29));
    }

    private static void UnboundedOccurrencesStayBounded()
    {
        var item = new CalendarItem
        {
            Title = "무기한 일정",
            StartDate = new DateOnly(2000, 1, 1),
            EndDate = new DateOnly(2000, 1, 1),
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Daily)
        };

        IReadOnlyList<CalendarOccurrence> occurrences =
            CalendarOccurrenceEngine.GetOccurrences(item,
                new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 12));

        AssertOccurrenceDates(occurrences,
            new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 11),
            new DateOnly(2026, 7, 12));
    }

    private static void UnsupportedRecurrencesAreRejected()
    {
        AssertThrows<ArgumentException>(() =>
            CalendarOccurrenceEngine.GetOccurrences(new CalendarItem
            {
                Title = "기간 반복",
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 2),
                Recurrence = new RecurrenceRule(RecurrenceFrequency.Daily)
            }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            "기간과 반복의 결합을 허용했습니다.");

        AssertThrows<ArgumentException>(() =>
            CalendarOccurrenceEngine.GetOccurrences(new CalendarItem
            {
                Title = "횟수 반복",
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 1),
                Recurrence = new RecurrenceRule(RecurrenceFrequency.Daily,
                    Count: 3)
            }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            "반복 횟수를 허용했습니다.");

        AssertThrows<ArgumentException>(() =>
            CalendarOccurrenceEngine.GetOccurrences(new CalendarItem
            {
                Title = "요일 없는 주간 반복",
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 1),
                Recurrence = new RecurrenceRule(RecurrenceFrequency.Weekly)
            }, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            "요일이 없는 주간 반복을 허용했습니다.");
    }

    private static void OccurrenceChangesApplyToWholeSeries() =>
        WithTempDirectory(directory =>
        {
            using var repository = new LocalCalendarRepository(directory);
            var item = new CalendarItem
            {
                Title = "전체 시리즈",
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 1),
                Recurrence = new RecurrenceRule(RecurrenceFrequency.Daily,
                    Until: new DateOnly(2026, 7, 3))
            };
            repository.UpsertItemAsync(item).GetAwaiter().GetResult();

            IReadOnlyList<CalendarOccurrence> original = repository
                .GetOccurrencesByRangeAsync(new DateOnly(2026, 7, 1),
                    new DateOnly(2026, 7, 3)).GetAwaiter().GetResult();
            Assert(original.Count == 3 &&
                   original.All(occurrence => occurrence.SeriesId == item.Id),
                "발생 일정이 원본 시리즈 ID를 보존하지 않았습니다.");

            CalendarItem editedSeries = original[1].Item;
            editedSeries.Title = "수정된 시리즈";
            editedSeries.IsCompleted = true;
            repository.UpsertItemAsync(editedSeries).GetAwaiter().GetResult();
            IReadOnlyList<CalendarOccurrence> edited = repository
                .GetOccurrencesByRangeAsync(new DateOnly(2026, 7, 1),
                    new DateOnly(2026, 7, 3)).GetAwaiter().GetResult();
            Assert(edited.Count == 3 && edited.All(occurrence =>
                    occurrence.Item.Title == "수정된 시리즈" &&
                    occurrence.Item.IsCompleted),
                "수정과 완료가 전체 시리즈에 적용되지 않았습니다.");

            repository.DeleteItemAsync(edited[0].SeriesId)
                .GetAwaiter().GetResult();
            Assert(repository.GetOccurrencesByRangeAsync(
                       new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3))
                       .GetAwaiter().GetResult().Count == 0,
                "삭제가 전체 시리즈에 적용되지 않았습니다.");
        });

    private static void OccurrencesAreDeterministicallyOrdered()
    {
        DateOnly date = new(2026, 7, 15);
        var items = new[]
        {
            new CalendarItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                Title = "완료",
                StartDate = date,
                EndDate = date,
                StartTime = new TimeOnly(8, 0),
                IsCompleted = true
            },
            new CalendarItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                Title = "나중",
                StartDate = date,
                EndDate = date,
                StartTime = new TimeOnly(10, 0)
            },
            new CalendarItem
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                Title = "먼저",
                StartDate = date,
                EndDate = date,
                StartTime = new TimeOnly(9, 0)
            }
        };

        IReadOnlyList<CalendarOccurrence> first =
            CalendarOccurrenceEngine.GetOccurrences(items, date, date);
        IReadOnlyList<CalendarOccurrence> second =
            CalendarOccurrenceEngine.GetOccurrences(items.Reverse(), date, date);
        Guid[] expectedOrder = [items[2].Id, items[1].Id, items[0].Id];

        Assert(first.Select(occurrence => occurrence.SeriesId)
                   .SequenceEqual(expectedOrder) &&
               second.Select(occurrence => occurrence.SeriesId)
                   .SequenceEqual(expectedOrder),
            "입력 순서에 따라 발생 일정 정렬 결과가 달라졌습니다.");
    }

    private static void AssertOccurrenceDates(
        IReadOnlyList<CalendarOccurrence> occurrences,
        params DateOnly[] expected) => Assert(
        occurrences.Select(occurrence => occurrence.Date)
            .SequenceEqual(expected),
        $"발생 날짜가 다릅니다. 실제: {string.Join(", ", occurrences.Select(
            occurrence => occurrence.Date))}");

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void TextColorsRequireAccessibleContrast()
    {
        TextColorValidation blue = CalendarTextColor.Validate("#0041e6");
        Assert(blue.IsValid && blue.NormalizedColor == "#0041E6" &&
               blue.ContrastRatio >= 4.5,
            "접근 가능한 색상을 정규화하거나 허용하지 못했습니다.");
        Assert(!CalendarTextColor.Validate("#FFFFFF").IsValid,
            "대비가 부족한 흰색을 허용했습니다.");
        Assert(!CalendarTextColor.Validate("0041E6").IsValid,
            "#이 없는 색상 형식을 허용했습니다.");
        string[] palette =
        [
            "#141A24", "#354153", "#0041E6", "#0A1491", "#7A2432",
            "#2F6B3C"
        ];
        Assert(palette.All(color => CalendarTextColor.Validate(color).IsValid),
            "접근성 팔레트에 WCAG AA를 만족하지 않는 색상이 있습니다.");
    }

    private static void DateBackgroundColorsAreNormalized() =>
        WithTempDirectory(directory =>
        {
            DateOnly date = new(2026, 7, 15);
            Assert(CalendarCellColor.Validate("#d0ebff") is
                    { IsValid: true, NormalizedColor: "#D0EBFF" },
                "날짜 배경색을 대문자 HEX로 정규화하지 못했습니다.");
            Assert(CalendarCellColor.GetForeground("#FFFFFF") ==
                   CalendarCellColor.LightForeground &&
                   CalendarCellColor.GetForeground("#141A24") ==
                   CalendarCellColor.DarkForeground,
                "밝고 어두운 배경에 읽기 쉬운 전경색을 선택하지 못했습니다.");
            Assert(CalendarCellColor.GetDecorationId(date) ==
                   CalendarCellColor.GetDecorationId(date) &&
                   CalendarCellColor.GetDecorationId(date) !=
                   CalendarCellColor.GetDecorationId(date.AddDays(1)),
                "날짜별 배경 ID가 결정적으로 생성되지 않았습니다.");

            using var repository = new LocalCalendarRepository(directory);
            var decoration = new DateCellDecoration
            {
                Id = CalendarCellColor.GetDecorationId(date),
                Date = date,
                Kind = DateCellDecorationKind.Highlight,
                Color = "#d0ebff",
                Label = "저장하지 않을 라벨"
            };
            repository.UpsertDecorationAsync(decoration).GetAwaiter().GetResult();
            DateCellDecoration stored = repository
                .GetDecorationsByRangeAsync(date, date).GetAwaiter()
                .GetResult().Single();
            Assert(stored.Color == "#D0EBFF" && stored.Label.Length == 0,
                "날짜 배경을 정규화해 직렬화하지 못했습니다.");
            decoration.Color = "D0EBFF";
            AssertThrows<ArgumentException>(() => repository
                .UpsertDecorationAsync(decoration).GetAwaiter().GetResult(),
                "잘못된 날짜 배경 HEX를 저장했습니다.");
        });

    private static void KoreanHolidaysMatchKnownDates()
    {
        IReadOnlyList<KoreanHoliday> holidays2025 =
            KoreanHolidayCalculator.GetHolidays(2025);
        IReadOnlyList<KoreanHoliday> holidays2026 =
            KoreanHolidayCalculator.GetHolidays(2026);

        AssertHoliday(holidays2025, new DateOnly(2025, 1, 28), "설날");
        AssertHoliday(holidays2025, new DateOnly(2025, 1, 29), "설날");
        AssertHoliday(holidays2025, new DateOnly(2025, 1, 30), "설날");
        AssertHoliday(holidays2025, new DateOnly(2025, 5, 5), "어린이날");
        AssertHoliday(holidays2025, new DateOnly(2025, 5, 5),
            "부처님오신날");
        AssertHoliday(holidays2025, new DateOnly(2025, 10, 6), "추석");
        AssertHoliday(holidays2026, new DateOnly(2026, 2, 17), "설날");
        AssertHoliday(holidays2026, new DateOnly(2026, 5, 1), "노동절");
        AssertHoliday(holidays2026, new DateOnly(2026, 7, 17), "제헌절");

        IReadOnlyDictionary<DateOnly, string> names =
            KoreanHolidayCalculator.GetHolidayNames(
                new DateOnly(2025, 5, 5), new DateOnly(2025, 5, 5));
        Assert(names[new DateOnly(2025, 5, 5)] ==
               "어린이날·부처님오신날",
            "같은 날 공휴일 이름을 안정적인 순서로 결합하지 못했습니다.");
        Assert(KoreanHolidayCalculator.GetHolidays(1899).Count == 0 &&
               KoreanHolidayCalculator.GetHolidays(2051).Count == 0,
            "지원 범위 밖 연도에서 빈 결과를 반환하지 않았습니다.");

        AssertHolidaySet(holidays2025,
            "2025-01-01|신정", "2025-01-28|설날", "2025-01-29|설날",
            "2025-01-30|설날", "2025-03-01|삼일절",
            "2025-03-03|대체공휴일(삼일절)", "2025-05-01|노동절",
            "2025-05-05|어린이날", "2025-05-05|부처님오신날",
            "2025-05-06|대체공휴일(부처님오신날)", "2025-06-06|현충일",
            "2025-07-17|제헌절", "2025-08-15|광복절",
            "2025-10-03|개천절", "2025-10-05|추석", "2025-10-06|추석",
            "2025-10-07|추석", "2025-10-08|대체공휴일(추석)",
            "2025-10-09|한글날", "2025-12-25|성탄절");
        AssertHolidaySet(holidays2026,
            "2026-01-01|신정", "2026-02-16|설날", "2026-02-17|설날",
            "2026-02-18|설날", "2026-03-01|삼일절",
            "2026-03-02|대체공휴일(삼일절)", "2026-05-01|노동절",
            "2026-05-05|어린이날", "2026-05-24|부처님오신날",
            "2026-05-25|대체공휴일(부처님오신날)", "2026-06-06|현충일",
            "2026-07-17|제헌절", "2026-08-15|광복절",
            "2026-08-17|대체공휴일(광복절)", "2026-09-24|추석",
            "2026-09-25|추석", "2026-09-26|추석", "2026-10-03|개천절",
            "2026-10-05|대체공휴일(개천절)", "2026-10-09|한글날",
            "2026-12-25|성탄절");
    }

    private static void KoreanSubstituteHolidaysAvoidCollisions()
    {
        IReadOnlyList<KoreanHoliday> holidays2025 =
            KoreanHolidayCalculator.GetHolidays(2025);
        IReadOnlyList<KoreanHoliday> holidays2026 =
            KoreanHolidayCalculator.GetHolidays(2026);
        IReadOnlyList<KoreanHoliday> holidays2027 =
            KoreanHolidayCalculator.GetHolidays(2027);

        AssertHoliday(holidays2025, new DateOnly(2025, 3, 3), "삼일절", true);
        AssertHoliday(holidays2025, new DateOnly(2025, 5, 6),
            "부처님오신날", true);
        AssertHoliday(holidays2025, new DateOnly(2025, 10, 8), "추석", true);
        AssertHoliday(holidays2026, new DateOnly(2026, 5, 25),
            "부처님오신날", true);
        AssertHoliday(holidays2026, new DateOnly(2026, 8, 17), "광복절", true);
        AssertHoliday(holidays2027, new DateOnly(2027, 5, 3), "노동절", true);
        AssertHoliday(holidays2027, new DateOnly(2027, 7, 19), "제헌절", true);
    }

    private static void CalendarViewModelComposesHolidays()
    {
        var repository = new InMemoryCalendarRepository();
        DateOnly chuseok = new(2025, 10, 6);
        repository.UpsertDecorationAsync(new DateCellDecoration
        {
            Id = CalendarCellColor.GetDecorationId(chuseok),
            Date = chuseok,
            Kind = DateCellDecorationKind.Highlight,
            Color = "#141A24"
        }).GetAwaiter().GetResult();
        var viewModel = new CalendarViewModel(repository,
            () => new DateTime(2025, 10, 1), ImmediateUpdate);

        viewModel.InitializeAsync().GetAwaiter().GetResult();
        CalendarDayViewModel holiday = viewModel.Days.Single(day =>
            day.Date == chuseok);
        CalendarDayViewModel substitute = viewModel.Days.Single(day =>
            day.Date == new DateOnly(2025, 10, 8));
        var foreground = holiday.DayForegroundBrush as
            Avalonia.Media.SolidColorBrush;
        var substituteForeground = substitute.DayForegroundBrush as
            Avalonia.Media.SolidColorBrush;
        Assert(holiday.IsHoliday && holiday.HolidayName == "추석",
            "새 달력 그리드에 공휴일을 합성하지 못했습니다.");
        Assert(substitute.IsHoliday &&
               substitute.HolidayName == "대체공휴일(추석)",
            "대체공휴일 표시 이름을 합성하지 못했습니다.");
        Assert(foreground?.Color == Avalonia.Media.Color.Parse("#FFFFFF"),
            "사용자 배경색 셀의 전경 우선순위를 지키지 않았습니다.");
        Assert(substituteForeground?.Color ==
               Avalonia.Media.Color.Parse("#FF5065"),
            "배경색이 없는 공휴일 날짜에 빨간 전경을 적용하지 않았습니다.");

        viewModel.RefreshAsync().GetAwaiter().GetResult();
        CalendarDayViewModel reused = viewModel.Days.Single(day =>
            day.Date == chuseok);
        Assert(ReferenceEquals(holiday, reused) && reused.IsHoliday &&
               reused.HolidayName == "추석",
            "재사용 달력 그리드 Update 경로가 공휴일을 보존하지 못했습니다.");

        viewModel.SelectYearAsync(2051).GetAwaiter().GetResult();
        Assert(viewModel.Days.All(day => !day.IsHoliday),
            "지원 범위 밖 연도로 이동했을 때 공휴일 표시가 남았습니다.");
    }

    private static void CalendarGridTracksWeekStart()
    {
        var viewModel = new CalendarViewModel(
            new InMemoryCalendarRepository(),
            () => new DateTime(2026, 2, 1), ImmediateUpdate);

        viewModel.InitializeAsync().GetAwaiter().GetResult();
        Assert(viewModel.WeekdayHeaders.Select(header => header.Text)
                   .SequenceEqual(["일", "월", "화", "수", "목", "금", "토"]) &&
               viewModel.Days[0].Date == new DateOnly(2026, 2, 1) &&
               viewModel.Days[^1].Date == new DateOnly(2026, 3, 14),
            "일요일 시작 2월 그리드 스냅샷이 올바르지 않습니다.");

        viewModel.SetDisplayOptions(CalendarWeekStart.Monday, true);
        viewModel.RefreshAsync().GetAwaiter().GetResult();
        Assert(viewModel.WeekdayHeaders.Select(header => header.Text)
                   .SequenceEqual(["월", "화", "수", "목", "금", "토", "일"]) &&
               viewModel.Days[0].Date == new DateOnly(2026, 1, 26) &&
               viewModel.Days[^1].Date == new DateOnly(2026, 3, 8),
            "월요일 시작 2월 그리드 스냅샷이 올바르지 않습니다.");

        viewModel.SelectMonthAsync(8).GetAwaiter().GetResult();
        Assert(viewModel.Days[0].Date == new DateOnly(2026, 7, 27) &&
               viewModel.Days[^1].Date == new DateOnly(2026, 9, 6),
            "월요일 시작 31일 월의 채움 셀이 올바르지 않습니다.");
        viewModel.SetDisplayOptions(CalendarWeekStart.Sunday, true);
        viewModel.RefreshAsync().GetAwaiter().GetResult();
        Assert(viewModel.Days[0].Date == new DateOnly(2026, 7, 26) &&
               viewModel.Days[^1].Date == new DateOnly(2026, 9, 5),
            "일요일 시작 31일 월의 채움 셀이 올바르지 않습니다.");
    }

    private static void CalendarDayForegroundPriority()
    {
        static Avalonia.Media.Color? Foreground(DateOnly date,
            string? background,
            string? holiday, bool colorWeekends)
        {
            var day = new CalendarDayViewModel(date, true, 0, 0, [], 3,
                background, holiday, colorWeekends);
            return (day.DayForegroundBrush as Avalonia.Media.SolidColorBrush)?
                .Color;
        }

        Assert(Foreground(new DateOnly(2026, 8, 1), null, null, true) ==
               Avalonia.Media.Color.Parse("#005AFF"),
            "토요일 날짜에 파란색을 적용하지 않았습니다.");
        Assert(Foreground(new DateOnly(2026, 8, 2), null, null, true) ==
               Avalonia.Media.Color.Parse("#FF5065"),
            "일요일 날짜에 빨간색을 적용하지 않았습니다.");
        Assert(Foreground(new DateOnly(2026, 8, 1), null, null, false) is null,
            "주말 색 끄기 설정에서 주말 전경이 남았습니다.");
        Assert(Foreground(new DateOnly(2026, 8, 1), null, "공휴일", true) ==
               Avalonia.Media.Color.Parse("#FF5065"),
            "토요일과 겹친 공휴일에 공휴일 색이 우선하지 않았습니다.");
        Assert(Foreground(new DateOnly(2026, 8, 1), "#141A24", "공휴일",
                   true) == Avalonia.Media.Color.Parse("#FFFFFF"),
            "사용자 배경색 전경이 공휴일과 주말 색보다 우선하지 않았습니다.");
        Assert(Foreground(new DateOnly(2026, 8, 3), null, null, true) is null,
            "평일에 불필요한 날짜 전경을 적용했습니다.");
    }

    private static void AssertHoliday(IReadOnlyList<KoreanHoliday> holidays,
        DateOnly date, string name, bool isSubstitute = false) =>
        Assert(holidays.Any(holiday => holiday.Date == date &&
                holiday.Name == name && holiday.IsSubstitute == isSubstitute),
            $"{date:yyyy-MM-dd} {name} 공휴일 계산이 올바르지 않습니다.");

    private static void AssertHolidaySet(
        IReadOnlyList<KoreanHoliday> holidays, params string[] expected)
    {
        string[] actual = holidays.Select(holiday =>
                $"{holiday.Date:yyyy-MM-dd}|{holiday.DisplayName}")
            .OrderBy(value => value).ToArray();
        Assert(actual.SequenceEqual(expected.OrderBy(value => value)),
            $"연간 공휴일 전체 집합이 다릅니다.\n예상: {string.Join(", ", expected)}" +
            $"\n실제: {string.Join(", ", actual)}");
    }

    private static void AppSettingsLoadLegacyBounds() =>
        WithTempDirectory(directory =>
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path,
                """{"SchemaVersion":1,"X":-240,"Y":75,"Width":860,"Height":540,"FutureValue":true}""");
            var store = new CalendarSettingsStore(path);

            Avalonia.PixelRect bounds = store.Load(980, 680);

            Assert(bounds == new Avalonia.PixelRect(-240, 75, 860, 540),
                "기존 창 위치 settings.json을 복원하지 못했습니다.");
            Assert(store.Current.SchemaVersion ==
                   AppSettings.CurrentSchemaVersion,
                "누락된 신규 필드의 기본값을 적용하지 못했습니다.");
        });

    private static void AppSettingsRoundTripAndNotify() =>
        WithTempDirectory(directory =>
        {
            string path = Path.Combine(directory, "settings.json");
            var store = new CalendarSettingsStore(path);
            var settings = new AppSettings
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                X = -80,
                Y = 120,
                Width = 900,
                Height = 620,
                WeekStart = CalendarWeekStart.Monday,
                ColorWeekends = false,
                FontScale = CalendarFontScale.Large,
                BackgroundOpacity = 0.65
            };
            int changeCount = 0;
            AppSettings? notified = null;
            store.Changed += (_, eventArgs) =>
            {
                changeCount++;
                notified = eventArgs.Settings;
            };

            store.Save(settings);
            store.Save(settings);

            AppSettings loaded = new CalendarSettingsStore(path)
                .LoadSettings();
            Assert(loaded == settings && notified == settings &&
                   changeCount == 1,
                "확장 설정 왕복 또는 변경 알림이 올바르지 않습니다.");
            Assert(!Directory.EnumerateFiles(directory, "*.tmp").Any(),
                "원자 저장 후 임시 파일이 남았습니다.");

            store.Save(new Avalonia.PixelRect(-20, 30, 700, 480));
            AppSettings resized = new CalendarSettingsStore(path)
                .LoadSettings();
            Assert(resized.SchemaVersion == settings.SchemaVersion &&
                   resized.X == -20 && resized.Y == 30 &&
                   resized.Width == 700 && resized.Height == 480,
                "기존 PixelRect 저장 호출이 확장 설정을 보존하지 못했습니다.");
        });

    private static void AppSettingsFallbackFromCorruption() =>
        WithTempDirectory(directory =>
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{ broken json");
            var store = new CalendarSettingsStore(path);

            Avalonia.PixelRect bounds = store.Load(980, 680);

            Assert(bounds == new Avalonia.PixelRect(100, 100, 980, 680) &&
                   store.Current == new AppSettings(),
                "손상된 설정 파일에서 기본값으로 복구하지 못했습니다.");
        });

    private static void SettingsResetWindowBounds() =>
        WithTempDirectory(directory =>
        {
            string path = Path.Combine(directory, "settings.json");
            var store = new CalendarSettingsStore(path);
            store.Save(new Avalonia.PixelRect(-320, 40, 760, 520));
            Avalonia.PixelRect applied = default;
            var viewModel = new SettingsViewModel("2.3.4", store, bounds =>
            {
                applied = bounds;
                return true;
            });

            bool reset = viewModel.ResetWindowPosition();
            Avalonia.PixelRect restored = new CalendarSettingsStore(path)
                .Load(980, 680);

            Assert(reset && applied == SettingsViewModel.DefaultWindowBounds &&
                   restored == SettingsViewModel.DefaultWindowBounds,
                "창 위치 초기화가 적용과 영구 저장을 함께 수행하지 못했습니다.");
            Assert(viewModel.HasStatus && !viewModel.HasError,
                "창 위치 초기화 성공 상태를 표시하지 못했습니다.");
        });

    private static void SettingsMenuTracksSession()
    {
        WithTempDirectory(directory =>
        {
            var viewModel = new SettingsViewModel("1.0.0",
                new CalendarSettingsStore(Path.Combine(directory,
                    "settings.json")), _ => true);

            Assert(viewModel.AuthenticationMenuText == "로그인 / 회원가입",
                "로그아웃 상태의 메뉴 문구가 올바르지 않습니다.");
            viewModel.UpdateSession(true);
            Assert(viewModel.AuthenticationMenuText == "로그아웃",
                "로그인 상태의 메뉴 문구가 올바르지 않습니다.");
        });
    }

    private static void AccountInputsAreValidated()
    {
        var session = new FakeAccountSession();
        var viewModel = new AccountViewModel(session)
        {
            CurrentPassword = "old-password",
            NewPassword = "new-password",
            ConfirmNewPassword = "different-password",
            DeletePassword = "old-password",
            DeleteConfirmation = "other@example.com"
        };

        Assert(!viewModel.CanChangePassword && !viewModel.CanDeleteAccount,
            "일치하지 않는 비밀번호나 이메일 확인을 허용했습니다.");
        viewModel.ConfirmNewPassword = "new-password";
        viewModel.DeleteConfirmation = session.User!.Email;
        Assert(viewModel.CanChangePassword && viewModel.CanDeleteAccount,
            "유효한 계정 입력을 허용하지 않았습니다.");
    }

    private static void AccountErrorsAreMapped()
    {
        var session = new FakeAccountSession
        {
            ChangeError = new AuthApiException(
                System.Net.HttpStatusCode.Unauthorized, "server message")
        };
        var viewModel = new AccountViewModel(session)
        {
            CurrentPassword = "old-password",
            NewPassword = "new-password",
            ConfirmNewPassword = "new-password"
        };

        bool changed = viewModel.ChangePasswordAsync().GetAwaiter().GetResult();
        Assert(!changed && viewModel.HasError &&
               viewModel.ErrorMessage == "현재 비밀번호가 일치하지 않습니다.",
            "401 계정 오류를 사용자 메시지로 매핑하지 못했습니다.");
    }

    private static void AccountSuccessEndsSession()
    {
        var changeSession = new FakeAccountSession();
        var change = new AccountViewModel(changeSession)
        {
            CurrentPassword = "old-password",
            NewPassword = "new-password",
            ConfirmNewPassword = "new-password"
        };
        Assert(change.ChangePasswordAsync().GetAwaiter().GetResult() &&
               change.SessionEnded && change.HasStatus &&
               changeSession.ChangedTo == "new-password",
            "비밀번호 변경 성공 상태나 재로그인 안내가 올바르지 않습니다.");

        var deleteSession = new FakeAccountSession();
        var delete = new AccountViewModel(deleteSession)
        {
            DeletePassword = "old-password",
            DeleteConfirmation = deleteSession.User!.Email
        };
        Assert(delete.DeleteAccountAsync().GetAwaiter().GetResult() &&
               delete.SessionEnded && deleteSession.DeletedWith == "old-password",
            "계정 삭제 성공 상태를 반영하지 못했습니다.");
    }

    private static void AccountScopeDeletionIsIsolated() =>
        WithTempDirectory(directory => AccountScopeDeletionCoreAsync(directory)
            .GetAwaiter().GetResult());

    private static async Task AccountScopeDeletionCoreAsync(string directory)
    {
        string accounts = Path.Combine(directory, "accounts");
        var userId = Guid.NewGuid();
        string selected = Path.Combine(accounts, userId.ToString("N"));
        string other = Path.Combine(accounts, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(selected);
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(selected, "selected.txt"), "delete");
        File.WriteAllText(Path.Combine(other, "other.txt"), "keep");

        using var session = new AuthSession("http://127.0.0.1:3000");
        await using var repository = new SyncingCalendarRepository(
            new LocalCalendarRepository(Path.Combine(accounts, "anonymous")),
            new RemoteCalendarRepository(session), session, accounts);
        await repository.SwitchScopeAsync(userId);
        await repository.DeleteAccountScopeAsync(userId);

        Assert(!Directory.Exists(selected) &&
               File.Exists(Path.Combine(other, "other.txt")),
            "선택한 로컬 계정 범위만 삭제하지 못했습니다.");
    }

    private static void CalendarDisplaySettingsPersist() =>
        WithTempDirectory(directory =>
        {
            string path = Path.Combine(directory, "settings.json");
            var store = new CalendarSettingsStore(path);
            store.LoadSettings();
            (CalendarWeekStart WeekStart, bool ColorWeekends)? applied = null;
            var viewModel = new SettingsViewModel("1.0.0", store, _ => true,
                applyDisplayOptions: (weekStart, colorWeekends) =>
                    applied = (weekStart, colorWeekends));

            viewModel.WeekStartIndex = 1;
            viewModel.ColorWeekends = false;
            AppSettings restored = new CalendarSettingsStore(path)
                .LoadSettings();

            Assert(restored.WeekStart == CalendarWeekStart.Monday &&
                   !restored.ColorWeekends &&
                   applied == (CalendarWeekStart.Monday, false),
                "달력 표시 설정을 즉시 적용하거나 다시 로드하지 못했습니다.");
        });

    private static void CalendarAppearanceCalculatesTokens()
    {
        CalendarAppearanceTokens small = CalendarAppearance.Create(
            CalendarFontScale.Small, 0.2);
        CalendarAppearanceTokens medium = CalendarAppearance.Create(
            CalendarFontScale.Medium, 0.9);
        CalendarAppearanceTokens large = CalendarAppearance.Create(
            CalendarFontScale.Large, 1.2);

        Assert(Math.Abs(small.DayFontSize - 12.6) < 0.001 &&
               Math.Abs(small.TaskRowHeight - 14.4) < 0.001 &&
               small.BackgroundOpacity == 0.5,
            "작은 글자 프리셋 또는 최소 불투명도 제한이 잘못되었습니다.");
        Assert(medium.HeaderFontSize == 16 && medium.TaskFontSize == 11 &&
               medium.BackgroundOpacity == 0.9,
            "보통 글자 프리셋 토큰이 기본 크기와 다릅니다.");
        Assert(Math.Abs(large.DayFontSize - 16.1) < 0.001 &&
               Math.Abs(large.TaskFontSize - 12.65) < 0.001 &&
               Math.Abs(large.CellHeaderHeight - 20.7) < 0.001 &&
               Math.Abs(large.TaskRowHeight - 18.4) < 0.001 &&
               large.BackgroundOpacity == 1,
            "큰 글자 프리셋 또는 최대 불투명도 제한이 잘못되었습니다.");
    }

    private static void CalendarAppearanceSettingsPersist() =>
        WithTempDirectory(directory =>
        {
            string path = Path.Combine(directory, "settings.json");
            var store = new CalendarSettingsStore(path);
            store.LoadSettings();
            (CalendarFontScale FontScale, double Opacity)? applied = null;
            var viewModel = new SettingsViewModel("1.0.0", store, _ => true,
                applyAppearance: (fontScale, opacity) =>
                    applied = (fontScale, opacity));

            viewModel.FontScaleIndex = 2;
            viewModel.BackgroundOpacity = 0.65;
            AppSettings restored = new CalendarSettingsStore(path)
                .LoadSettings();

            Assert(restored.FontScale == CalendarFontScale.Large &&
                   restored.BackgroundOpacity == 0.65 &&
                   applied == (CalendarFontScale.Large, 0.65) &&
                   viewModel.BackgroundOpacityText == "65%",
                "외형 설정을 즉시 적용하거나 다시 로드하지 못했습니다.");
        });

    private static void AutoStartRegistryRoundTrip()
    {
        if (!OperatingSystem.IsWindows()) return;
        string valueName = $"HmDesktopCalendar.Tests.{Guid.NewGuid():N}";
        const string processPath = @"C:\Program Files\Hm Calendar\HmDesktopCalendar.exe";
        var registrar = new AutoStartRegistrar(valueName, processPath);
        try
        {
            AutoStartStatus enabled = registrar.SetEnabled(true);
            using Microsoft.Win32.RegistryKey? key =
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    AutoStartRegistrar.RunKeyPath, false);
            string? stored = key?.GetValue(valueName) as string;
            Assert(enabled.IsAvailable && enabled.IsEnabled &&
                   registrar.GetStatus().IsEnabled &&
                   stored == $"\"{processPath}\"",
                "자동 시작 Run 값을 인용된 실행 파일 경로로 등록하지 못했습니다.");

            AutoStartStatus disabled = registrar.SetEnabled(false);
            Assert(disabled.IsAvailable && !disabled.IsEnabled &&
                   !registrar.GetStatus().IsEnabled,
                "자동 시작 Run 값을 삭제하지 못했습니다.");
        }
        finally { registrar.SetEnabled(false); }
    }

    private static void AutoStartFailureDisablesToggle()
    {
        var viewModel = new SettingsViewModel("1.0.0",
            new CalendarSettingsStore(Path.Combine(Path.GetTempPath(),
                $"hm-calendar-{Guid.NewGuid():N}.json")), _ => true,
            autoStartRegistrar: new UnavailableAutoStartRegistrar());

        Assert(!viewModel.IsAutoStartAvailable &&
               !viewModel.IsAutoStartEnabled && viewModel.HasAutoStartError &&
               viewModel.AutoStartError.Contains("테스트 오류"),
            "자동 시작 접근 실패 상태를 설정 화면에 전달하지 못했습니다.");
    }

    private static void BackupCopiesDataOncePerDay() =>
        WithTempDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "calendar-v2.json"),
                "root-data");
            File.WriteAllText(Path.Combine(directory, "settings.json"),
                "excluded");
            string account = Path.Combine(directory, "accounts", "user-a");
            Directory.CreateDirectory(account);
            File.WriteAllText(Path.Combine(account, "calendar-v2.json"),
                "account-data");
            DateTimeOffset now = new(2026, 7, 16, 9, 30, 0,
                TimeSpan.FromHours(9));
            var service = new BackupService(directory, () => now);

            BackupResult first = service.CreateBackupIfDueAsync()
                .GetAwaiter().GetResult();
            BackupResult second = service.CreateBackupIfDueAsync()
                .GetAwaiter().GetResult();

            Assert(first.Created && !second.Created &&
                   Directory.GetDirectories(service.BackupRoot).Length == 1,
                "같은 날 중복 백업을 생성했습니다.");
            Assert(File.ReadAllText(Path.Combine(first.BackupPath!,
                       "calendar-v2.json")) == "root-data" &&
                   File.ReadAllText(Path.Combine(first.BackupPath!, "accounts",
                       "user-a", "calendar-v2.json")) == "account-data" &&
                   !File.Exists(Path.Combine(first.BackupPath!, "settings.json")),
                "백업 대상 범위 또는 상대 경로가 올바르지 않습니다.");
        });

    private static void BackupRetainsTenGenerations() =>
        WithTempDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "calendar-v2.json"), "x");
            string backupRoot = Path.Combine(directory, "backups");
            Directory.CreateDirectory(backupRoot);
            for (int day = 1; day <= 10; day++)
                Directory.CreateDirectory(Path.Combine(backupRoot,
                    $"202606{day:00}-010000"));
            var service = new BackupService(directory, () =>
                new DateTimeOffset(2026, 7, 16, 12, 0, 0,
                    TimeSpan.FromHours(9)));

            BackupResult result = service.CreateBackupIfDueAsync()
                .GetAwaiter().GetResult();
            string[] generations = Directory.GetDirectories(backupRoot)
                .Select(Path.GetFileName).OrderBy(name => name).ToArray()!;

            Assert(result.Created && generations.Length == 10 &&
                   !generations.Contains("20260601-010000") &&
                   generations.Contains("20260716-120000"),
                "11번째 백업에서 가장 오래된 세대를 삭제하지 못했습니다.");
        });

    private static void BackupFailureLeavesNoPartialGeneration() =>
        WithTempDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "calendar-v2.json"), "x");
            var service = new BackupService(directory,
                () => new DateTimeOffset(2026, 7, 16, 12, 0, 0,
                    TimeSpan.FromHours(9)),
                (_, _, _) => throw new IOException("copy failed"));

            BackupResult result = service.CreateBackupIfDueAsync()
                .GetAwaiter().GetResult();

            Assert(!result.Succeeded && !result.Created &&
                   Directory.GetDirectories(service.BackupRoot).Length == 0,
                "복사 실패 후 부분 백업 세대가 남았습니다.");
        });

    private static void LegacyTextColorIsMigrated() =>
        WithTempDirectory(directory =>
        {
            DateOnly date = new(2026, 7, 15);
            File.WriteAllText(Path.Combine(directory, "calendar-v2.json"),
                JsonSerializer.Serialize(new
                {
                    Version = 2,
                    Items = new[]
                    {
                        new CalendarItem
                        {
                            Title = "이전 색상 일정",
                            StartDate = date,
                            EndDate = date,
                            Color = CalendarTextColor.LegacyDefaultColor
                        }
                    },
                    Decorations = Array.Empty<DateCellDecoration>(),
                    SyncState = new SyncState(0)
                }));
            using var repository = new LocalCalendarRepository(directory);
            CalendarItem migrated = repository.GetItemsByRangeAsync(date, date)
                .GetAwaiter().GetResult().Single();
            Assert(migrated.Color == CalendarTextColor.DefaultColor,
                "이전 기본 텍스트 색상을 접근 가능한 기본값으로 바꾸지 않았습니다.");
            migrated.Color = "#FFFFFF";
            AssertThrows<ArgumentException>(() => repository
                .UpsertItemAsync(migrated).GetAwaiter().GetResult(),
                "저장소가 대비가 부족한 텍스트 색상을 허용했습니다.");
        });

    private static void EditorDraftPreservesSeriesFields()
    {
        DateOnly date = new(2026, 7, 15);
        var source = new CalendarItem
        {
            Title = "원본 일정",
            Notes = "원본 메모",
            StartDate = date,
            EndDate = date,
            Color = "#141A24",
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Weekly, 2,
                [DayOfWeek.Wednesday], new DateOnly(2026, 12, 31)),
            Reminders = [new CalendarReminder(30)]
        };
        var draft = new CalendarEditorDraftViewModel();

        draft.BeginEdit(source);
        Assert(!draft.HasUnsavedChanges && draft.IsEditing,
            "원본을 불러오자마자 변경된 것으로 표시했습니다.");
        draft.Title = "수정 일정";
        draft.Notes = "수정 메모";
        draft.TimeValue = new TimeSpan(9, 30, 0);
        draft.IsCompleted = true;
        draft.Color = "#0a1491";
        Assert(draft.HasUnsavedChanges && draft.CanSave,
            "유효한 편집 변경을 저장 가능 상태로 만들지 못했습니다.");

        CalendarItem edited = draft.CreateItem();
        Assert(edited.Id == source.Id && edited.Title == "수정 일정" &&
               edited.Notes == "수정 메모" &&
               edited.StartTime == new TimeOnly(9, 30) &&
               edited.IsCompleted && edited.Color == "#0A1491",
            "편집 가능한 필드를 일정 원본에 반영하지 못했습니다.");
        Assert(edited.Recurrence?.Frequency == source.Recurrence?.Frequency &&
               edited.Recurrence?.Interval == source.Recurrence?.Interval &&
               edited.Recurrence?.Until == source.Recurrence?.Until &&
               edited.Recurrence?.DaysOfWeek?.SequenceEqual(
                   source.Recurrence?.DaysOfWeek ?? []) == true &&
               edited.Reminders.SequenceEqual(source.Reminders) &&
               edited.StartDate == source.StartDate &&
               edited.EndDate == source.EndDate,
            "편집하지 않는 시리즈 필드를 보존하지 못했습니다.");
    }

    private static void AnniversaryDraftCreatesYearlySeries()
    {
        DateOnly leapDate = new(2024, 2, 29);
        var draft = new CalendarEditorDraftViewModel();
        draft.BeginNew(leapDate);
        draft.Title = "창립 기념일";
        draft.IsCompleted = true;
        draft.IsAnniversary = true;

        CalendarItem item = draft.CreateItem();
        Assert(item.IsAnniversary && !item.IsCompleted &&
               item.Recurrence is
               { Frequency: RecurrenceFrequency.Yearly, Interval: 1,
                 Until: null, Count: null },
            "기념일을 완료 없는 무기한 연간 반복으로 만들지 못했습니다.");
        AssertOccurrenceDates(CalendarOccurrenceEngine.GetOccurrences(item,
            new DateOnly(2025, 1, 1), new DateOnly(2028, 12, 31)),
            new DateOnly(2028, 2, 29));

        item.IsCompleted = true;
        AssertThrows<ArgumentException>(() =>
            CalendarOccurrenceEngine.GetOccurrences(item, leapDate, leapDate),
            "완료된 기념일을 도메인 규칙이 허용했습니다.");
    }

    private static void EditorModeTransitionsResetUnusedFields()
    {
        DateOnly date = new(2026, 7, 15);
        var draft = new CalendarEditorDraftViewModel();
        draft.BeginNew(date);
        draft.Title = "모드 전환";

        draft.SetMode(CalendarEditMode.Range);
        Assert(draft.IsRangeMode && draft.EndDateValue?.Date ==
               date.AddDays(1).ToDateTime(TimeOnly.MinValue),
            "기간 모드가 기본 종료일을 만들지 않았습니다.");
        draft.EndDateValue = new DateTimeOffset(2026, 7, 20, 0, 0, 0,
            TimeSpan.Zero);

        draft.SetMode(CalendarEditMode.Recurring);
        Assert(draft.IsRecurringMode && draft.EndDateValue is null &&
               draft.RecurrenceFrequencyIndex ==
                   (int)RecurrenceFrequency.Daily &&
               draft.RecurrenceInterval == 1 &&
               !draft.HasRecurrenceUntil,
            "반복 모드가 기간 전용 필드와 반복 기본값을 초기화하지 않았습니다.");
        draft.RecurrenceFrequencyIndex = (int)RecurrenceFrequency.Weekly;
        Assert(draft.Wednesday,
            "주간 반복이 시작 날짜의 요일을 기본 선택하지 않았습니다.");
        draft.HasRecurrenceUntil = true;

        draft.SetMode(CalendarEditMode.Single);
        Assert(draft.IsSingleMode && draft.EndDateValue is null &&
               draft.RecurrenceFrequencyIndex ==
                   (int)RecurrenceFrequency.Daily &&
               draft.RecurrenceInterval == 1 && !draft.Sunday &&
               !draft.Monday && !draft.Tuesday && !draft.Wednesday &&
               !draft.Thursday && !draft.Friday && !draft.Saturday &&
               !draft.HasRecurrenceUntil &&
               draft.RecurrenceUntilValue is null,
            "단일 모드가 기간·반복 전용 필드를 초기화하지 않았습니다.");
    }

    private static void EditorSeriesValidationRejectsInvalidRules()
    {
        DateOnly date = new(2026, 7, 15);
        var draft = new CalendarEditorDraftViewModel();
        draft.BeginNew(date);
        draft.Title = "유효성 검사";
        draft.SetMode(CalendarEditMode.Range);
        draft.EndDateValue = new DateTimeOffset(2026, 7, 15, 0, 0, 0,
            TimeSpan.Zero);
        Assert(draft.ValidationMessage.Contains("시작일보다 늦어야"),
            "시작일과 같은 기간 종료일을 허용했습니다.");

        draft.SetMode(CalendarEditMode.Recurring);
        draft.RecurrenceInterval = 0;
        Assert(draft.ValidationMessage.Contains("1 이상의 정수"),
            "0인 반복 간격을 허용했습니다.");
        draft.RecurrenceInterval = 1.5m;
        Assert(draft.ValidationMessage.Contains("1 이상의 정수"),
            "소수 반복 간격을 허용했습니다.");
        draft.RecurrenceInterval = 1;
        draft.RecurrenceFrequencyIndex = (int)RecurrenceFrequency.Weekly;
        draft.Wednesday = false;
        Assert(draft.ValidationMessage.Contains("요일을 한 개 이상"),
            "요일 없는 주간 반복을 허용했습니다.");
        draft.Monday = true;
        draft.HasRecurrenceUntil = true;
        draft.RecurrenceUntilValue = new DateTimeOffset(2026, 7, 14,
            0, 0, 0, TimeSpan.Zero);
        Assert(draft.ValidationMessage.Contains("빠를 수 없습니다"),
            "시작일보다 빠른 반복 종료일을 허용했습니다.");
    }

    private static void EditorRecurrencesRoundTrip()
    {
        DateOnly date = new(2026, 7, 15);
        foreach (RecurrenceFrequency frequency in
                 Enum.GetValues<RecurrenceFrequency>())
        {
            var draft = new CalendarEditorDraftViewModel();
            draft.BeginNew(date);
            draft.Title = $"{frequency} 반복";
            draft.SetMode(CalendarEditMode.Recurring);
            draft.RecurrenceFrequencyIndex = (int)frequency;
            draft.RecurrenceInterval = 2;
            if (frequency == RecurrenceFrequency.Weekly)
            {
                draft.Wednesday = false;
                draft.Monday = true;
                draft.Friday = true;
            }
            draft.HasRecurrenceUntil = true;
            draft.RecurrenceUntilValue = new DateTimeOffset(2027, 7, 15,
                0, 0, 0, TimeSpan.Zero);

            CalendarItem item = draft.CreateItem();
            var reopened = new CalendarEditorDraftViewModel();
            reopened.BeginEdit(item);
            CalendarItem restored = reopened.CreateItem();
            bool weekdaysMatch = frequency == RecurrenceFrequency.Weekly
                ? restored.Recurrence?.DaysOfWeek?.SequenceEqual(
                    [DayOfWeek.Monday, DayOfWeek.Friday]) == true
                : restored.Recurrence?.DaysOfWeek is null;
            Assert(reopened.IsRecurringMode &&
                   restored.Recurrence?.Frequency == frequency &&
                   restored.Recurrence?.Interval == 2 &&
                   restored.Recurrence?.Until == new DateOnly(2027, 7, 15) &&
                   weekdaysMatch,
                $"{frequency} 반복 규칙을 다시 열어 복원하지 못했습니다.");
        }

        var rangeDraft = new CalendarEditorDraftViewModel();
        rangeDraft.BeginNew(date);
        rangeDraft.Title = "월 경계 기간";
        rangeDraft.SetMode(CalendarEditMode.Range);
        rangeDraft.EndDateValue = new DateTimeOffset(2026, 8, 2,
            0, 0, 0, TimeSpan.Zero);
        CalendarItem range = rangeDraft.CreateItem();
        var reopenedRange = new CalendarEditorDraftViewModel();
        reopenedRange.BeginEdit(range);
        Assert(reopenedRange.IsRangeMode &&
               reopenedRange.CreateItem().EndDate == new DateOnly(2026, 8, 2),
            "월 경계 기간 일정을 다시 열어 복원하지 못했습니다.");
    }

    private static void DerivedDateEditorUpdatesWholeSeries()
    {
        var repository = new InMemoryCalendarRepository();
        DateOnly start = new(2026, 7, 1);
        var series = new CalendarItem
        {
            Title = "매주 회의",
            StartDate = start,
            EndDate = start,
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Weekly,
                1, [DayOfWeek.Wednesday])
        };
        repository.UpsertItemAsync(series).GetAwaiter().GetResult();
        DateOnly derivedDate = new(2026, 7, 15);
        using var viewModel = new CalendarEditorViewModel(
            derivedDate, repository, ImmediateUpdate);
        viewModel.LoadAsync().GetAwaiter().GetResult();
        CalendarItem occurrenceSource = viewModel.Items.Single();
        Assert(occurrenceSource.Id == series.Id &&
               occurrenceSource.StartDate == start,
            "파생 날짜에서 시리즈 원본을 불러오지 못했습니다.");

        viewModel.BeginEdit(occurrenceSource);
        Assert(viewModel.Draft.StartDateText == "2026년 7월 1일" &&
               viewModel.Draft.ShowsSeriesScopeNotice,
            "파생 날짜 편집에 원본 시작일과 전체 시리즈 안내를 표시하지 않았습니다.");
        viewModel.Draft.Title = "수정된 매주 회의";
        viewModel.Draft.IsCompleted = true;
        Assert(viewModel.SaveDraftAsync().GetAwaiter().GetResult() &&
               viewModel.Items.Single().Id == series.Id &&
               viewModel.Items.Single().Title == "수정된 매주 회의" &&
               viewModel.Items.Single().IsCompleted,
            "파생 날짜 수정·완료를 전체 시리즈 원본에 적용하지 못했습니다.");
        Assert(viewModel.DeleteAsync(viewModel.Items.Single())
                   .GetAwaiter().GetResult() && viewModel.Items.Count == 0,
            "파생 날짜 삭제를 전체 시리즈 원본에 적용하지 못했습니다.");
    }

    private static void DateBackgroundEditorConverges()
    {
        var repository = new InMemoryCalendarRepository();
        DateOnly date = new(2026, 7, 15);
        using var viewModel = new CalendarEditorViewModel(
            date, repository, ImmediateUpdate);
        viewModel.LoadAsync().GetAwaiter().GetResult();
        viewModel.Background.SetPaletteColor("#343a40");
        Assert(viewModel.SaveBackgroundAsync().GetAwaiter().GetResult(),
            "날짜 배경을 저장하지 못했습니다.");
        DateCellDecoration first = repository
            .GetDecorationsByRangeAsync(date, date).GetAwaiter()
            .GetResult().Single();
        Assert(first.Id == CalendarCellColor.GetDecorationId(date) &&
               first.Kind == DateCellDecorationKind.Highlight,
            "날짜 배경을 결정적 동기화 엔터티로 저장하지 않았습니다.");

        viewModel.Background.SetPaletteColor("#D0EBFF");
        viewModel.SaveBackgroundAsync().GetAwaiter().GetResult();
        IReadOnlyList<DateCellDecoration> updated = repository
            .GetDecorationsByRangeAsync(date, date).GetAwaiter().GetResult();
        Assert(updated.Count == 1 && updated[0].Id == first.Id &&
               updated[0].Color == "#D0EBFF",
            "배경 수정이 같은 동기화 엔터티에 수렴하지 않았습니다.");

        viewModel.Background.Clear();
        viewModel.SaveBackgroundAsync().GetAwaiter().GetResult();
        Assert(repository.GetDecorationsByRangeAsync(date, date).GetAwaiter()
                   .GetResult().Count == 0,
            "배경 초기화가 동기화 엔터티를 삭제하지 않았습니다.");
    }

    private static void EditorNavigationProtectsUnsavedChanges()
    {
        var repository = new InMemoryCalendarRepository();
        DateOnly firstDate = new(2026, 7, 15);
        repository.UpsertItemAsync(new CalendarItem
        {
            Title = "첫 일정", StartDate = firstDate, EndDate = firstDate
        }).GetAwaiter().GetResult();
        repository.UpsertItemAsync(new CalendarItem
        {
            Title = "둘째 일정", StartDate = firstDate, EndDate = firstDate
        }).GetAwaiter().GetResult();
        using var viewModel = new CalendarEditorViewModel(
            firstDate, repository, ImmediateUpdate);
        Assert(viewModel.LoadAsync().GetAwaiter().GetResult(),
            "초기 편집 날짜를 불러오지 못했습니다.");
        viewModel.BeginEdit(viewModel.Items[0]);
        viewModel.Draft.Title = "저장 전 일정";
        Guid? editingId = viewModel.Draft.SourceId;
        Assert(!viewModel.BeginEdit(viewModel.Items[1]) &&
               viewModel.Draft.SourceId == editingId,
            "저장하지 않은 변경을 버리고 다른 일정을 선택했습니다.");

        bool blocked = viewModel.LoadDateAsync(new DateOnly(2026, 7, 16))
            .GetAwaiter().GetResult();
        Assert(!blocked && viewModel.Date == firstDate &&
               viewModel.Draft.Title == "저장 전 일정",
            "저장하지 않은 변경을 버리고 날짜를 이동했습니다.");
        bool discarded = viewModel.LoadDateAsync(new DateOnly(2026, 7, 16),
            true).GetAwaiter().GetResult();
        Assert(discarded && viewModel.Date == new DateOnly(2026, 7, 16) &&
               !viewModel.Draft.HasUnsavedChanges,
            "명시적 취소 뒤 날짜를 이동하지 못했습니다.");
    }

    private static void EditorOperationsDoNotDuplicateItems()
    {
        var repository = new InMemoryCalendarRepository();
        using var viewModel = new CalendarEditorViewModel(
            new DateOnly(2026, 7, 15), repository, ImmediateUpdate);
        viewModel.LoadAsync().GetAwaiter().GetResult();
        viewModel.Draft.Title = "새 일정";
        viewModel.Draft.Notes = "중요 메모";
        viewModel.Draft.TimeValue = new TimeSpan(12, 0, 0);
        viewModel.Draft.Color = "#7A2432";
        Assert(viewModel.SaveDraftAsync().GetAwaiter().GetResult() &&
               repository.UpsertCount == 1 && viewModel.Items.Count == 1,
            "새 일정을 정확히 한 번 저장하지 못했습니다.");

        CalendarItem item = viewModel.Items[0];
        Assert(viewModel.BeginEdit(item), "기존 일정 편집을 시작하지 못했습니다.");
        viewModel.Draft.Title = "수정 일정";
        Assert(viewModel.SaveDraftAsync().GetAwaiter().GetResult() &&
               repository.UpsertCount == 2 && viewModel.Items.Count == 1 &&
               viewModel.Items[0].Title == "수정 일정",
            "수정 저장이 일정을 중복 생성했습니다.");

        item = viewModel.Items[0];
        viewModel.BeginEdit(item);
        viewModel.Draft.IsCompleted = true;
        Assert(viewModel.SaveDraftAsync().GetAwaiter().GetResult() &&
               viewModel.Items.Count == 1 && viewModel.Items[0].IsCompleted,
            "완료 상태 저장이 일정을 중복 생성했습니다.");

        Assert(viewModel.DeleteAsync(viewModel.Items[0])
                   .GetAwaiter().GetResult() &&
               repository.DeleteCount == 1 && viewModel.Items.Count == 0,
            "삭제가 원본 일정 하나에 적용되지 않았습니다.");
    }

    private static void CalendarPreviewUsesItemColor()
    {
        var repository = new InMemoryCalendarRepository();
        repository.UpsertItemAsync(new CalendarItem
        {
            Title = "색상 일정",
            StartDate = new DateOnly(2026, 7, 15),
            EndDate = new DateOnly(2026, 7, 15),
            Color = "#7A2432"
        }).GetAwaiter().GetResult();
        var viewModel = new CalendarViewModel(repository,
            () => new DateTime(2026, 7, 14), ImmediateUpdate);
        viewModel.InitializeAsync().GetAwaiter().GetResult();

        CalendarTaskPreviewViewModel preview = viewModel.Days
            .Single(day => day.Date == new DateOnly(2026, 7, 15))
            .AllTasks.Single();
        var brush = preview.TextBrush as Avalonia.Media.SolidColorBrush;
        Assert(brush?.Color == Avalonia.Media.Color.Parse("#7A2432"),
            "달력 미리보기에 일정 텍스트 색상이 반영되지 않았습니다.");
    }

    private static void CalendarPreviewShowsBackgroundAndAnniversary()
    {
        var repository = new InMemoryCalendarRepository();
        DateOnly date = new(2026, 7, 15);
        repository.UpsertItemAsync(new CalendarItem
        {
            Title = "창립 기념일",
            Kind = CalendarItemKind.Anniversary,
            StartDate = date,
            EndDate = date,
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Yearly)
        }).GetAwaiter().GetResult();
        repository.UpsertItemAsync(new CalendarItem
        {
            Title = "일반 일정", StartDate = date, EndDate = date
        }).GetAwaiter().GetResult();
        repository.UpsertDecorationAsync(new DateCellDecoration
        {
            Id = CalendarCellColor.GetDecorationId(date),
            Date = date,
            Kind = DateCellDecorationKind.Highlight,
            Color = "#343A40"
        }).GetAwaiter().GetResult();

        var viewModel = new CalendarViewModel(repository,
            () => new DateTime(2026, 7, 15), ImmediateUpdate);
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        CalendarDayViewModel day = viewModel.Days.Single(item =>
            item.Date == date);
        var background = day.CellBackgroundBrush as
            Avalonia.Media.SolidColorBrush;
        var foreground = day.CellForegroundBrush as
            Avalonia.Media.SolidColorBrush;
        Assert(day.IncompleteCount == 1 && day.CompletedCount == 0 &&
               day.AllTasks.Count == 2 && day.AllTasks[0].IsAnniversary &&
               background?.Color == Avalonia.Media.Color.Parse("#343A40") &&
               foreground?.Color == Avalonia.Media.Color.Parse("#FFFFFF"),
            "날짜 배경, 자동 전경 또는 기념일 배지를 달력에 반영하지 못했습니다.");
    }

    private static void ServerMergePreservesPendingLocalChanges() =>
        WithTempDirectory(directory =>
        {
            using var repository = new LocalCalendarRepository(directory);
            DateOnly date = new(2026, 7, 15);
            var local = new CalendarItem
            {
                Title = "로컬 변경",
                StartDate = date,
                EndDate = date
            };
            repository.UpsertItemAsync(local).GetAwaiter().GetResult();
            CalendarItem pending = repository.GetAllItemsAsync()
                .GetAwaiter().GetResult().Single();
            var uploaded = new CalendarItem
            {
                Id = local.Id,
                Title = "서버 반영",
                StartDate = date,
                EndDate = date,
                Revision = 2,
                Cursor = 10,
                UpdatedAt = pending.UpdatedAt.AddSeconds(1)
            };

            repository.ApplyServerAsync([uploaded], [])
                .GetAwaiter().GetResult();
            Assert(repository.GetAllItemsAsync().GetAwaiter().GetResult()
                       .Single().Title == "로컬 변경",
                "전송 전 로컬 변경을 서버 변경으로 덮어썼습니다.");

            repository.MarkItemUploadedAsync(uploaded, pending.UpdatedAt)
                .GetAwaiter().GetResult();
            CalendarItem synchronized = repository.GetAllItemsAsync()
                .GetAwaiter().GetResult().Single();
            Assert(synchronized.Title == "서버 반영" &&
                   synchronized.Revision == 2 && synchronized.Cursor == 10,
                "업로드 확인 결과를 로컬 일정에 반영하지 못했습니다.");

            uploaded.Title = "다른 환경 변경";
            uploaded.Revision = 3;
            uploaded.Cursor = 11;
            repository.ApplyServerAsync([uploaded], [])
                .GetAwaiter().GetResult();
            Assert(repository.GetAllItemsAsync().GetAwaiter().GetResult()
                       .Single().Title == "다른 환경 변경",
                "동기화된 일정에 최신 서버 변경을 반영하지 못했습니다.");
        });

    private static void CalendarScopesStayIsolated() =>
        WithTempDirectory(directory =>
        {
            string root = Path.Combine(directory, "root");
            string accounts = Path.Combine(directory, "accounts");
            var local = new LocalCalendarRepository(root);
            local.UpsertItemAsync(new CalendarItem
            {
                Title = "익명 일정",
                StartDate = new DateOnly(2026, 7, 15),
                EndDate = new DateOnly(2026, 7, 15)
            }).GetAwaiter().GetResult();
            using var session = new AuthSession("http://127.0.0.1:3000");
            using var syncing = new SyncingCalendarRepository(local,
                new RemoteCalendarRepository(session), session, accounts);

            syncing.SwitchScopeAsync(null).GetAwaiter().GetResult();
            Assert(syncing.GetItemsByRangeAsync(DateOnly.MinValue,
                       DateOnly.MaxValue).GetAwaiter().GetResult().Count == 1,
                "최초 익명 범위에 기존 로컬 데이터를 옮기지 못했습니다.");
            syncing.SwitchScopeAsync(Guid.Parse(
                "00000000-0000-0000-0000-000000000001"))
                .GetAwaiter().GetResult();
            Assert(syncing.GetItemsByRangeAsync(DateOnly.MinValue,
                       DateOnly.MaxValue).GetAwaiter().GetResult().Count == 1,
                "최초 로그인 계정에 익명 일정을 가져오지 못했습니다.");
            syncing.SwitchScopeAsync(Guid.Parse(
                "00000000-0000-0000-0000-000000000002"))
                .GetAwaiter().GetResult();
            Assert(syncing.GetItemsByRangeAsync(DateOnly.MinValue,
                       DateOnly.MaxValue).GetAwaiter().GetResult().Count == 0,
                "서로 다른 로그인 계정 사이에 일정이 섞였습니다.");
        });

    private static void RemoteCalendarClientUsesV2Contract()
    {
        var requests = new List<string>();
        var handler = new RecordingHttpMessageHandler(async (request,
            cancellationToken) =>
        {
            Assert(request.Headers.Authorization?.Scheme == "Bearer" &&
                   request.Headers.Authorization.Parameter == "access-token",
                "v2 원격 요청에 접근 토큰을 넣지 않았습니다.");
            requests.Add($"{request.Method} {request.RequestUri?.PathAndQuery} " +
                (request.Content is null ? string.Empty :
                    await request.Content.ReadAsStringAsync(cancellationToken)));
            string json = request.Method == HttpMethod.Put
                ? """
                  {"id":"00000000-0000-0000-0000-000000000010",
                   "kind":"schedule","title":"원격 일정","notes":"메모",
                   "startDate":"2026-07-15","endDate":"2026-07-15",
                   "startTime":"09:30","endTime":null,"allDay":false,
                   "completed":false,"color":"#0041E6","recurrence":{
                     "frequency":"weekly","interval":2,"daysOfWeek":[1,5],
                     "until":"2026-12-31","count":null},
                   "reminders":[],"deleted":false,"revision":2,"cursor":6,
                   "updatedAt":"2026-07-15T00:00:00Z"}
                  """
                : """
                  {"changes":[
                    {"entityType":"calendarItem","cursor":7,"payload":{
                      "id":"00000000-0000-0000-0000-000000000010",
                      "kind":"schedule","title":"다른 환경 일정","notes":"",
                      "startDate":"2026-07-16","endDate":"2026-07-16",
                      "startTime":null,"endTime":null,"allDay":true,
                      "completed":false,"color":"#141A24","recurrence":{
                        "frequency":"monthly","interval":1,"daysOfWeek":[],
                        "until":null,"count":null},
                      "reminders":[],"deleted":false,"revision":3,
                      "updatedAt":"2026-07-15T01:00:00Z"}},
                    {"entityType":"dateCellDecoration","cursor":8,"payload":{
                      "id":"00000000-0000-0000-0000-000000000020",
                      "date":"2026-07-16","kind":"colorDot","color":"#FF0000",
                      "label":"휴일","deleted":false,"revision":1,
                      "updatedAt":"2026-07-15T01:00:00Z"}}],
                   "nextCursor":8,"hasMore":false}
                  """;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8,
                    "application/json")
            };
        });
        using var remote = new RemoteCalendarRepository(
            _ => Task.FromResult<string?>("access-token"),
            "http://calendar.test", handler);
        DateOnly date = new(2026, 7, 15);
        CalendarItem uploaded = remote.UpsertItemAsync(new CalendarItem
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000010"),
            Title = "원격 일정",
            Notes = "메모",
            StartDate = date,
            EndDate = date,
            StartTime = new TimeOnly(9, 30),
            Color = "#0041E6",
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Weekly, 2,
                [DayOfWeek.Monday, DayOfWeek.Friday],
                new DateOnly(2026, 12, 31))
        }).GetAwaiter().GetResult();
        Assert(uploaded.Revision == 2 && uploaded.Cursor == 6 &&
               uploaded.StartTime == new TimeOnly(9, 30) &&
               uploaded.Recurrence is
               { Frequency: RecurrenceFrequency.Weekly, Interval: 2 } &&
               uploaded.Recurrence.DaysOfWeek?.SequenceEqual(
                   [DayOfWeek.Monday, DayOfWeek.Friday]) == true &&
               uploaded.Recurrence.Until == new DateOnly(2026, 12, 31),
            "v2 일정 저장 응답을 CalendarItem으로 변환하지 못했습니다.");

        CalendarSyncPage page = remote.PullAsync(6).GetAwaiter().GetResult();
        Assert(page.NextCursor == 8 && page.Items.Single().Title ==
               "다른 환경 일정" && page.Items.Single().Cursor == 7 &&
               page.Items.Single().Recurrence?.Frequency ==
                   RecurrenceFrequency.Monthly &&
               page.Decorations.Single().Cursor == 8,
            "v2 통합 커서 응답을 일정과 날짜 장식으로 분리하지 못했습니다.");
        Assert(requests[0].StartsWith("PUT /v2/calendar-items/") &&
               requests[0].Contains("\"color\":\"#0041E6\"") &&
               requests[0].Contains("\"frequency\":\"weekly\"") &&
               requests[0].Contains("\"daysOfWeek\":[1,5]") &&
               requests[1] == "GET /v2/sync?after=6&limit=500 ",
            "v2 원격 요청 경로나 일정 payload가 잘못되었습니다.");
    }

    private static void ReminderEditorCreatesAnchors()
    {
        var draft = new CalendarEditorDraftViewModel();
        draft.BeginNew(new DateOnly(2026, 7, 15));
        draft.Title = "시간 없는 알림";
        draft.ReminderEnabled = true;
        draft.ReminderPresetIndex = 5;
        draft.ReminderTimeValue = new TimeSpan(8, 30, 0);
        CalendarItem item = draft.CreateItem();
        Assert(item.Reminders.Single() == new CalendarReminder(1440,
                   new TimeOnly(8, 30)),
            "시간 없는 일정의 1일 전 알림 기준 시각을 저장하지 못했습니다.");

        draft.TimeValue = new TimeSpan(10, 0, 0);
        draft.ReminderPresetIndex = 6;
        draft.CustomReminderMinutes = 90;
        item = draft.CreateItem();
        Assert(item.Reminders.Single() == new CalendarReminder(90),
            "시간 있는 일정의 사용자 지정 알림을 시작 시각 기준으로 저장하지 못했습니다.");
    }

    private static void ReminderRepositoryValidatesRules() =>
        WithTempDirectory(directory =>
        {
            using var repository = new LocalCalendarRepository(directory);
            CalendarItem CreateItem() => new()
            {
                Title = "검증 일정",
                StartDate = new DateOnly(2026, 7, 15),
                EndDate = new DateOnly(2026, 7, 15)
            };
            CalendarItem allDay = CreateItem();
            allDay.Reminders = [new CalendarReminder(15)];
            AssertThrows<ArgumentException>(() => repository.UpsertItemAsync(
                allDay).GetAwaiter().GetResult(),
                "시간 없는 일정에서 알림 기준 시각 누락을 허용했습니다.");

            CalendarItem timed = CreateItem();
            timed.StartTime = new TimeOnly(9, 0);
            timed.Reminders = [new CalendarReminder(15, new TimeOnly(8, 0))];
            AssertThrows<ArgumentException>(() => repository.UpsertItemAsync(
                timed).GetAwaiter().GetResult(),
                "시간 있는 일정에 별도 알림 기준 시각을 허용했습니다.");

            CalendarItem excessive = CreateItem();
            excessive.StartTime = new TimeOnly(9, 0);
            excessive.Reminders = [new CalendarReminder(525601)];
            AssertThrows<ArgumentException>(() => repository.UpsertItemAsync(
                excessive).GetAwaiter().GetResult(),
                "최대 범위를 넘는 알림 간격을 허용했습니다.");
        });

    private static void ReminderSchedulerHandlesLifecycle() =>
        WithTempDirectory(directory =>
        {
            using var repository = new LocalCalendarRepository(directory);
            var state = new MemoryReminderStateStore();
            var clock = new FakeReminderClock(new DateTimeOffset(
                2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
            var scheduler = new ReminderScheduler(repository, state, clock,
                () => "account", TimeZoneInfo.Utc);
            repository.UpsertItemAsync(new CalendarItem
            {
                Title = "기간 일정",
                StartDate = new DateOnly(2026, 7, 15),
                EndDate = new DateOnly(2026, 7, 17),
                StartTime = new TimeOnly(10, 0),
                Reminders = [new CalendarReminder(0), new CalendarReminder(5),
                    new CalendarReminder(15), new CalendarReminder(30),
                    new CalendarReminder(60), new CalendarReminder(90),
                    new CalendarReminder(1440)]
            }).GetAwaiter().GetResult();
            repository.UpsertItemAsync(new CalendarItem
            {
                Title = "시간 없는 일정",
                StartDate = new DateOnly(2026, 7, 15),
                EndDate = new DateOnly(2026, 7, 15),
                Reminders = [new CalendarReminder(5, new TimeOnly(10, 5))]
            }).GetAwaiter().GetResult();
            repository.UpsertItemAsync(new CalendarItem
            {
                Title = "반복 일정",
                StartDate = new DateOnly(2026, 7, 15),
                EndDate = new DateOnly(2026, 7, 15),
                StartTime = new TimeOnly(10, 0),
                Recurrence = new RecurrenceRule(RecurrenceFrequency.Daily),
                Reminders = [new CalendarReminder(0)]
            }).GetAwaiter().GetResult();

            IReadOnlyList<ReminderNotification> first = scheduler.ScanAsync()
                .GetAwaiter().GetResult();
            Assert(first.Count == 9 && first.Count(notification =>
                       notification.Item.Title == "기간 일정") == 7 &&
                   first.Count(notification =>
                       notification.Item.Title == "반복 일정") == 1,
                "프리셋·사용자 지정·반복 또는 기간 시작일 알림을 잘못 계산했습니다.");
            Assert(scheduler.ScanAsync().GetAwaiter().GetResult().Count == 0,
                "이미 표시한 알림을 다시 표시했습니다.");

            ReminderNotification snoozed = first.Single(notification =>
                notification.Item.Title == "시간 없는 일정");
            scheduler.SnoozeAsync(snoozed, 5).GetAwaiter().GetResult();
            clock.Now = clock.Now.AddMinutes(5);
            IReadOnlyList<ReminderNotification> afterSnooze = scheduler.ScanAsync()
                .GetAwaiter().GetResult();
            Assert(afterSnooze.Count == 1 && afterSnooze[0].IsSnoozed,
                "5분 다시 알림을 지정한 시각에 한 번 표시하지 못했습니다.");

            repository.UpsertItemAsync(new CalendarItem
            {
                Title = "24시간 밖 알림",
                StartDate = new DateOnly(2026, 7, 14),
                EndDate = new DateOnly(2026, 7, 14),
                StartTime = new TimeOnly(9, 59),
                Reminders = [new CalendarReminder(0)]
            }).GetAwaiter().GetResult();
            Assert(scheduler.ScanAsync().GetAwaiter().GetResult().Count == 0,
                "24시간보다 오래된 놓친 알림을 복구했습니다.");
            scheduler.Start();
            scheduler.StopAsync().GetAwaiter().GetResult();
            scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();
        });

    private sealed class FakeReminderClock(DateTimeOffset now) : IReminderClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private sealed class MemoryReminderStateStore : IReminderDeviceStateStore
    {
        private readonly Dictionary<string, ReminderDeviceState> _states = [];
        public Task<IReadOnlyDictionary<string, ReminderDeviceState>> ReadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ReminderDeviceState>>(
                new Dictionary<string, ReminderDeviceState>(_states));
        public Task WriteAsync(string key, ReminderDeviceState state,
            CancellationToken cancellationToken = default)
        {
            _states[key] = state;
            return Task.CompletedTask;
        }
    }

    private sealed class UnavailableAutoStartRegistrar : IAutoStartRegistrar
    {
        public AutoStartStatus GetStatus() =>
            AutoStartStatus.Unavailable("테스트 오류");
        public AutoStartStatus SetEnabled(bool enabled) => GetStatus();
    }

    private static Task ImmediateUpdate(Action update)
    {
        update();
        return Task.CompletedTask;
    }

    private static void WithTempDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(),
            $"hm-calendar-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    private static void CalendarViewModelMovesPredictably()
    {
        var viewModel = CreateCalendarViewModel();
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2026 && viewModel.CurrentMonth == 7,
            "초기 표시 월이 주입한 오늘과 다릅니다.");

        viewModel.SelectYearAsync(2030).GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2030 && viewModel.CurrentMonth == 7,
            "연도 선택이 현재 월을 보존하지 않았습니다.");

        viewModel.SelectMonthAsync(2).GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2030 && viewModel.CurrentMonth == 2,
            "월 선택이 현재 연도를 보존하지 않았습니다.");

        viewModel.PreviousMonthAsync().GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2030 && viewModel.CurrentMonth == 1,
            "이전 달 이동 결과가 잘못되었습니다.");
        viewModel.PreviousMonthAsync().GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2029 && viewModel.CurrentMonth == 12,
            "이전 달 이동이 연도 경계를 처리하지 못했습니다.");

        viewModel.GoToTodayAsync().GetAwaiter().GetResult();
        Assert(viewModel.CurrentYear == 2026 && viewModel.CurrentMonth == 7,
            "오늘 바로가기가 주입한 오늘로 돌아오지 않았습니다.");
    }

    private static void SynchronizationStatePreservesLastSuccess()
    {
        var viewModel = CreateCalendarViewModel();
        viewModel.SetSynchronizationAvailability(false);
        Assert(viewModel.SynchronizationStatus == "로컬 전용",
            "로그아웃 상태가 로컬 전용으로 표시되지 않았습니다.");

        viewModel.SetSynchronizationAvailability(true);
        viewModel.ApplySynchronizationState(new CalendarSynchronizationState(
            CalendarSynchronizationStatus.InProgress,
            new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.Zero)));
        Assert(viewModel.IsSynchronizing &&
               viewModel.SynchronizationStatus == "동기화 중…",
            "진행 상태가 올바르게 표시되지 않았습니다.");

        var succeededAt = new DateTimeOffset(2026, 7, 14, 12, 34, 0,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 7, 14)));
        viewModel.ApplySynchronizationState(new CalendarSynchronizationState(
            CalendarSynchronizationStatus.Succeeded, succeededAt));
        Assert(!viewModel.IsSynchronizing &&
               viewModel.SynchronizationStatus.Contains("마지막 성공 12:34"),
            "마지막 성공 시각이 표시되지 않았습니다.");

        viewModel.ApplySynchronizationState(new CalendarSynchronizationState(
            CalendarSynchronizationStatus.Failed, succeededAt.AddMinutes(2),
            "network"));
        Assert(viewModel.IsSynchronizationFailed &&
               viewModel.SynchronizationStatus.Contains("동기화 실패") &&
               viewModel.SynchronizationStatus.Contains("마지막 성공 12:34"),
            "실패 상태가 마지막 성공 시각을 보존하지 않았습니다.");

        viewModel.SetSynchronizationAvailability(false);
        viewModel.SetSynchronizationAvailability(true);
        Assert(!viewModel.SynchronizationStatus.Contains("12:34"),
            "로그아웃 뒤 이전 계정의 성공 시각이 남았습니다.");
    }

    private static void IcsSerializesEventsAndPeriods()
    {
        var exporter = new IcsExporter();
        string actual = exporter.Export(
        [
            new CalendarItem
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Title = "휴가, 여름",
                Notes = "첫째 날; 둘째 날",
                StartDate = new DateOnly(2026, 7, 20),
                EndDate = new DateOnly(2026, 7, 22),
                IsAllDay = true,
                UpdatedAt = new DateTimeOffset(2026, 7, 16, 1, 2, 3,
                    TimeSpan.Zero)
            },
            new CalendarItem
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Title = "회의",
                StartDate = new DateOnly(2026, 7, 23),
                EndDate = new DateOnly(2026, 7, 23),
                StartTime = new TimeOnly(9, 30),
                EndTime = new TimeOnly(10, 30),
                UpdatedAt = new DateTimeOffset(2026, 7, 16, 2, 3, 4,
                    TimeSpan.Zero)
            }
        ]);
        const string expected = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" +
            "PRODID:-//HmDesktopCalendar//KO\r\nCALSCALE:GREGORIAN\r\n" +
            "METHOD:PUBLISH\r\nX-WR-TIMEZONE:Asia/Seoul\r\n" +
            "BEGIN:VTIMEZONE\r\nTZID:Asia/Seoul\r\nBEGIN:STANDARD\r\n" +
            "DTSTART:19700101T000000\r\nTZOFFSETFROM:+0900\r\n" +
            "TZOFFSETTO:+0900\r\nTZNAME:KST\r\nEND:STANDARD\r\n" +
            "END:VTIMEZONE\r\nBEGIN:VEVENT\r\n" +
            "UID:11111111111111111111111111111111@hm-desktop-calendar\r\n" +
            "DTSTAMP:20260716T010203Z\r\nDTSTART;VALUE=DATE:20260720\r\n" +
            "DTEND;VALUE=DATE:20260723\r\nSUMMARY:휴가\\, 여름\r\n" +
            "DESCRIPTION:첫째 날\\; 둘째 날\r\nEND:VEVENT\r\n" +
            "BEGIN:VEVENT\r\n" +
            "UID:22222222222222222222222222222222@hm-desktop-calendar\r\n" +
            "DTSTAMP:20260716T020304Z\r\n" +
            "DTSTART;TZID=Asia/Seoul:20260723T093000\r\n" +
            "DTEND;TZID=Asia/Seoul:20260723T103000\r\n" +
            "SUMMARY:회의\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        Assert(actual == expected,
            "VEVENT 스냅샷 또는 기간 DTEND(exclusive)가 다릅니다.");
    }

    private static void IcsPreservesRecurrenceRules()
    {
        DateOnly start = new(2026, 7, 16);
        var items = new[]
        {
            NewRecurring("매일", RecurrenceFrequency.Daily, 2),
            NewRecurring("매주", RecurrenceFrequency.Weekly, 3,
                [DayOfWeek.Monday, DayOfWeek.Friday]),
            NewRecurring("매월", RecurrenceFrequency.Monthly, 1),
            NewRecurring("매년", RecurrenceFrequency.Yearly, 1)
        };
        string ics = new IcsExporter().Export(items);
        Assert(ics.Contains("RRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20270831T145959Z\r\n"),
            "일일 반복 RRULE이 다릅니다.");
        Assert(ics.Contains("RRULE:FREQ=WEEKLY;INTERVAL=3;BYDAY=MO,FR;" +
            "UNTIL=20270831T145959Z\r\n"),
            "주간 반복 RRULE이 요일을 보존하지 못했습니다.");
        Assert(ics.Contains("RRULE:FREQ=MONTHLY;UNTIL=20270831T145959Z\r\n") &&
               ics.Contains("RRULE:FREQ=YEARLY;UNTIL=20270831T145959Z\r\n"),
            "월간 또는 연간 반복 RRULE이 다릅니다.");

        CalendarItem NewRecurring(string title, RecurrenceFrequency frequency,
            int interval, IReadOnlyList<DayOfWeek>? days = null) => new()
            {
                Title = title,
                StartDate = start,
                EndDate = start,
                StartTime = new TimeOnly(9, 0),
                Recurrence = new RecurrenceRule(frequency, interval, days,
                    new DateOnly(2027, 8, 31)),
                UpdatedAt = DateTimeOffset.UnixEpoch
            };
    }

    private static void IcsFoldsUtf8AndWritesAtomically() =>
        WithTempDirectory(directory =>
        {
            string title = string.Concat(Enumerable.Repeat("한글,줄;\\", 20));
            var item = new CalendarItem
            {
                Title = title,
                Notes = "첫 줄\r\n둘째 줄",
                StartDate = new DateOnly(2026, 7, 16),
                EndDate = new DateOnly(2026, 7, 16),
                IsAllDay = true,
                UpdatedAt = DateTimeOffset.UnixEpoch
            };
            var exporter = new IcsExporter();
            string ics = exporter.Export([item]);
            string[] physicalLines = ics.Split("\r\n",
                StringSplitOptions.RemoveEmptyEntries);
            Assert(physicalLines.All(line => Encoding.UTF8.GetByteCount(line) <= 75),
                "ICS 물리 줄이 75옥텟을 초과했습니다.");
            Assert(physicalLines.Any(line => line.StartsWith(' ')),
                "긴 UTF-8 줄이 folding되지 않았습니다.");
            Assert(ics.Contains("DESCRIPTION:첫 줄\\n둘째 줄\r\n"),
                "줄바꿈 텍스트가 이스케이프되지 않았습니다.");

            string path = Path.Combine(directory, "calendar.ics");
            exporter.ExportToFileAsync([item], path).GetAwaiter().GetResult();
            Assert(File.ReadAllText(path, Encoding.UTF8) == ics,
                "원자 저장 파일 내용이 직렬화 결과와 다릅니다.");

            File.WriteAllText(path, "기존 파일", Encoding.UTF8);
            var invalid = new CalendarItem
            {
                Title = "잘못된 일정",
                StartDate = new DateOnly(2026, 7, 17),
                EndDate = new DateOnly(2026, 7, 16)
            };
            try
            {
                exporter.ExportToFileAsync([invalid], path).GetAwaiter().GetResult();
                throw new InvalidOperationException("잘못된 일정 저장이 성공했습니다.");
            }
            catch (ArgumentException)
            {
            }
            Assert(File.ReadAllText(path, Encoding.UTF8) == "기존 파일",
                "저장 실패가 기존 파일을 손상했습니다.");
            Assert(Directory.GetFiles(directory, "*.tmp").Length == 0,
                "저장 실패 뒤 임시 파일이 남았습니다.");
        });

    private static void IcsExportUsesAllSeries() =>
        WithTempDirectory(directory =>
        {
            var repository = new InMemoryCalendarRepository();
            repository.UpsertItemAsync(new CalendarItem
            {
                Title = "현재 범위 일정",
                StartDate = new DateOnly(2026, 7, 20),
                EndDate = new DateOnly(2026, 7, 20)
            }).GetAwaiter().GetResult();
            repository.UpsertItemAsync(new CalendarItem
            {
                Title = "먼 미래 원본",
                StartDate = new DateOnly(2030, 1, 1),
                EndDate = new DateOnly(2030, 1, 1)
            }).GetAwaiter().GetResult();
            using var viewModel = new ScheduleOverviewViewModel(repository,
                () => new DateTime(2026, 7, 16), ImmediateUpdate, Task.Delay);
            viewModel.SearchText = "현재 범위 일정";
            string path = Path.Combine(directory, "all-series.ics");
            viewModel.ExportIcsAsync(path).GetAwaiter().GetResult();

            string ics = File.ReadAllText(path, Encoding.UTF8);
            Assert(ics.Contains("SUMMARY:현재 범위 일정\r\n") &&
                   ics.Contains("SUMMARY:먼 미래 원본\r\n"),
                "화면 필터 또는 조회 기간이 ICS 범위를 줄였습니다.");
            Assert(viewModel.ExportMessage.Contains("2개") &&
                   !viewModel.IsExportError,
                "ICS 내보내기 성공 상태가 잘못되었습니다.");
        });

    private static void ScheduleOverviewCombinesFilters()
    {
        var repository = new InMemoryCalendarRepository();
        DateTime today = new(2026, 7, 15);
        repository.UpsertItemAsync(new CalendarItem
        {
            Title = "프로젝트 회의", Notes = "고객 요청 검토",
            StartDate = new DateOnly(2026, 7, 16),
            EndDate = new DateOnly(2026, 7, 16),
            StartTime = new TimeOnly(9, 0)
        }).GetAwaiter().GetResult();
        repository.UpsertItemAsync(new CalendarItem
        {
            Title = "정산 완료", IsCompleted = true,
            StartDate = new DateOnly(2026, 7, 15),
            EndDate = new DateOnly(2026, 7, 15),
            StartTime = new TimeOnly(18, 0)
        }).GetAwaiter().GetResult();
        repository.UpsertItemAsync(new CalendarItem
        {
            Kind = CalendarItemKind.Anniversary,
            Title = "창립 기념일",
            StartDate = new DateOnly(2020, 7, 20),
            EndDate = new DateOnly(2020, 7, 20),
            Recurrence = new RecurrenceRule(RecurrenceFrequency.Yearly)
        }).GetAwaiter().GetResult();
        repository.UpsertItemAsync(new CalendarItem
        {
            Title = "범위 밖 일정",
            StartDate = new DateOnly(2026, 11, 1),
            EndDate = new DateOnly(2026, 11, 1)
        }).GetAwaiter().GetResult();

        using var viewModel = new ScheduleOverviewViewModel(repository,
            () => today, action => { action(); return Task.CompletedTask; },
            Task.Delay);
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        Assert(viewModel.Items.Count == 3,
            "기본 90일 범위가 잘못되었습니다.");

        viewModel.SearchText = "고객";
        viewModel.RefreshAsync().GetAwaiter().GetResult();
        Assert(viewModel.Items.Count == 1 &&
               viewModel.Items[0].Title == "프로젝트 회의",
            "제목·메모 검색이 결과를 좁히지 못했습니다.");

        viewModel.SearchText = string.Empty;
        viewModel.StatusIndex = (int)ScheduleOverviewStatus.Completed;
        viewModel.RefreshAsync().GetAwaiter().GetResult();
        Assert(viewModel.Items.Count == 1 && viewModel.Items[0].IsCompleted,
            "완료 필터가 완료 일정만 표시하지 못했습니다.");

        viewModel.StatusIndex = (int)ScheduleOverviewStatus.All;
        viewModel.RangeIndex = (int)ScheduleOverviewRange.Custom;
        viewModel.CustomStart = new DateTimeOffset(2026, 7, 20, 0, 0, 0,
            TimeSpan.Zero);
        viewModel.CustomEnd = new DateTimeOffset(2026, 7, 15, 0, 0, 0,
            TimeSpan.Zero);
        viewModel.SortIndex = 1;
        viewModel.RefreshAsync().GetAwaiter().GetResult();
        Assert(viewModel.Items.Count == 3 &&
               viewModel.Items[0].Date == new DateOnly(2026, 7, 20) &&
               viewModel.Items[^1].Date == new DateOnly(2026, 7, 15),
            "사용자 기간 정규화 또는 날짜·시간 역순 정렬이 잘못되었습니다.");
    }

    private static void ScheduleOverviewDebouncesRepositoryChanges()
    {
        var repository = new InMemoryCalendarRepository();
        var delays = new List<TaskCompletionSource>();
        Task Delay(TimeSpan _, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetCanceled(
                cancellationToken));
            delays.Add(completion);
            return completion.Task;
        }

        using var viewModel = new ScheduleOverviewViewModel(repository,
            () => new DateTime(2026, 7, 15),
            action => { action(); return Task.CompletedTask; }, Delay);
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        int initialQueries = repository.OccurrenceQueryCount;
        repository.RaiseChanged();
        repository.RaiseChanged();
        repository.RaiseChanged();
        Assert(delays.Count == 3, "저장소 변경 알림이 누락되었습니다.");
        delays[^1].SetResult();
        Assert(SpinWait.SpinUntil(() => repository.OccurrenceQueryCount ==
                   initialQueries + 1, TimeSpan.FromSeconds(2)),
            "마지막 변경 뒤 목록을 새로고치지 않았습니다.");
        Assert(repository.OccurrenceQueryCount == initialQueries + 1,
            "연속 변경이 여러 조회로 증폭되었습니다.");
    }

    private static CalendarViewModel CreateCalendarViewModel() => new(
        new InMemoryCalendarRepository(),
        () => new DateTime(2026, 7, 14),
        update =>
        {
            update();
            return Task.CompletedTask;
        });

    private static void BoundsDoesNotChangeLayer()
    {
        var native = new FakeWindowNativeApi();
        native.Parents[native.Calendar] = native.Host;
        native.NextWindows[native.IconView] = native.Calendar;
        var service = new WindowBoundsService(native);
        var target = new ScreenWindowBounds(-880, 337, 1080, 969);

        Assert(service.ApplyAttached(native.Calendar, native.Host, target,
            out _), "Bounds 적용이 실패했습니다.");
        NativePositionCall call = native.PositionCalls.Last();
        Assert(call.Flags.HasFlag(WindowPositionFlags.NoZOrder),
            "Bounds 적용에 SWP_NOZORDER가 없습니다.");
        Assert(call.InsertAfter == IntPtr.Zero,
            "Bounds 서비스가 레이어 핸들을 사용했습니다.");
        Assert(native.Bounds[native.Calendar] == target,
            "음수 화면 Bounds가 정확히 유지되지 않았습니다.");
        Assert(native.NextWindows[native.IconView] == native.Calendar,
            "Bounds 적용이 기존 Z 순서를 변경했습니다.");
    }

    private static void LayerDoesNotChangeBounds()
    {
        var native = new FakeWindowNativeApi();
        native.Parents[native.Calendar] = native.Host;
        var original = new ScreenWindowBounds(-880, 337, 1080, 969);
        native.Bounds[native.Calendar] = original;
        var service = new DesktopLayerService(native);
        var shell = new DesktopShellHandles(native.Host, native.IconView);

        Assert(service.EnsureIconsAboveCalendar(native.Calendar, shell,
            out _), "레이어 적용이 실패했습니다.");
        NativePositionCall call = native.PositionCalls.Last();
        Assert(call.Flags.HasFlag(WindowPositionFlags.NoMove) &&
               call.Flags.HasFlag(WindowPositionFlags.NoSize),
            "레이어 적용이 이동·크기 보존 플래그를 사용하지 않았습니다.");
        Assert(!call.Flags.HasFlag(WindowPositionFlags.NoZOrder),
            "레이어 적용이 Z 순서 변경을 차단했습니다.");
        Assert(native.Bounds[native.Calendar] == original,
            "레이어 적용이 Bounds를 변경했습니다.");
        Assert(service.AreIconsAboveCalendar(native.Calendar, shell),
            "아이콘이 달력 위에 배치되지 않았습니다.");
    }

    private static void ForegroundEditingIsNotTopmost()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결에 실패했습니다.");
        setup.Native.PositionCalls.Clear();

        WindowTransitionResult result =
            setup.Coordinator.BeginForegroundEditing();

        Assert(result.Success, result.Message);
        NativePositionCall activation = setup.Native.PositionCalls.Last();
        Assert(activation.InsertAfter == NativeWindowHandles.Top,
            "수정 창 활성화에 HWND_TOPMOST가 사용되었습니다.");
        Assert(activation.InsertAfter != NativeWindowHandles.Topmost,
            "수정 창이 영구 Topmost 상태가 되었습니다.");
    }

    private static void CommitPreservesBothInvariants()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결이 실패했습니다.");

        for (int index = 0; index < 3; index++)
        {
            Assert(setup.Coordinator.BeginForegroundEditing().Success,
                "전경 수정 모드 전환이 실패했습니다.");
            var desired = new ScreenWindowBounds(-880 + index * 25,
                337 + index * 30, 1080, 969);
            setup.Native.Bounds[setup.Native.Calendar] = desired;
            PixelRectLike(setup.Coordinator.CaptureCurrentBounds(), desired);
            WindowTransitionResult result =
                setup.Coordinator.TryCommitDesktop(desired.ToPixelRect());
            Assert(result.Success, result.Message);
            Assert(setup.Native.Bounds[setup.Native.Calendar] == desired,
                "저장 후 Bounds가 이동했습니다.");
            Assert(result.State.IconsAboveCalendar,
                "저장 후 달력이 아이콘 위로 올라왔습니다.");
            Assert(result.State.ParentAttached && result.State.BoundsMatch,
                "저장 후 부모 또는 Bounds 불변식이 깨졌습니다.");
        }
    }

    private static void FailureRollsBackToEditing()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결이 실패했습니다.");
        Assert(setup.Coordinator.BeginForegroundEditing().Success,
            "전경 수정 모드 전환이 실패했습니다.");
        var desired = new ScreenWindowBounds(-700, 500, 900, 800);
        setup.Native.Bounds[setup.Native.Calendar] = desired;
        setup.Native.FailLayerChange = true;

        WindowTransitionResult failed =
            setup.Coordinator.TryCommitDesktop(desired.ToPixelRect());
        Assert(!failed.Success, "레이어 실패인데 저장 전환이 성공했습니다.");
        Assert(setup.Coordinator.Mode ==
               DesktopCalendarWindowMode.ForegroundEditing,
            "실패 후 수정 모드가 유지되지 않았습니다.");
        Assert(setup.Native.Parents[setup.Native.Calendar] == IntPtr.Zero,
            "실패 후 전경 창으로 롤백되지 않았습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == desired,
            "실패 롤백 과정에서 Bounds가 변경되었습니다.");

        setup.Native.FailLayerChange = false;
        WindowTransitionResult retried =
            setup.Coordinator.TryCommitDesktop(desired.ToPixelRect());
        Assert(retried.Success, "재시도 저장이 실패했습니다.");
    }

    private static void VisibleNegativeBoundsArePreserved()
    {
        var native = new FakeWindowNativeApi();
        var service = new WindowBoundsService(native);
        var visible = new ScreenWindowBounds(-880, 337, 1080, 969);
        Assert(service.RecoverIfFullyOffscreen(visible, 700, 480) == visible,
            "화면과 겹치는 음수 좌표가 보정되었습니다.");
        var outside = new ScreenWindowBounds(-5000, -5000, 900, 700);
        Assert(service.RecoverIfFullyOffscreen(outside, 700, 480) != outside,
            "완전히 화면 밖인 Bounds가 복구되지 않았습니다.");
    }

    private static void MaintenanceRepairsOnlyBrokenInvariant()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결이 실패했습니다.");
        ScreenWindowBounds expected =
            setup.Native.Bounds[setup.Native.Calendar];

        setup.Native.PositionCalls.Clear();
        setup.Native.Bounds[setup.Native.Calendar] = expected with
            { X = expected.X + 150 };
        DesktopWindowState boundsRepair =
            setup.Coordinator.MaintainDesktopState();
        Assert(boundsRepair.IsValid, "Bounds 단독 복구가 실패했습니다.");
        Assert(setup.Native.PositionCalls.Any(call =>
                call.Flags.HasFlag(WindowPositionFlags.NoZOrder) &&
                !call.Flags.HasFlag(WindowPositionFlags.NoMove)),
            "Bounds 손상에 Bounds 서비스가 사용되지 않았습니다.");
        Assert(setup.Native.PositionCalls.All(call =>
                call.InsertAfter != setup.Native.IconView),
            "Bounds 단독 복구가 레이어를 변경했습니다.");

        setup.Native.PositionCalls.Clear();
        setup.Native.NextWindows[setup.Native.IconView] = IntPtr.Zero;
        DesktopWindowState layerRepair =
            setup.Coordinator.MaintainDesktopState();
        Assert(layerRepair.IsValid, "레이어 단독 복구가 실패했습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == expected,
            "레이어 단독 복구가 Bounds를 변경했습니다.");
        Assert(setup.Native.PositionCalls.Any(call =>
                call.InsertAfter == setup.Native.IconView &&
                call.Flags.HasFlag(WindowPositionFlags.NoMove) &&
                call.Flags.HasFlag(WindowPositionFlags.NoSize)),
            "레이어 손상에 레이어 서비스가 사용되지 않았습니다.");

        setup.Native.Parents[setup.Native.Calendar] = IntPtr.Zero;
        DesktopWindowState parentRepair =
            setup.Coordinator.MaintainDesktopState();
        Assert(parentRepair.IsValid, "부모 손상 전체 복구가 실패했습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == expected,
            "부모 재연결 과정에서 Bounds가 변경되었습니다.");
    }

    private static void CancelRestoresOriginalBounds()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결에 실패했습니다.");
        ScreenWindowBounds original = setup.Native.Bounds[setup.Native.Calendar];
        Assert(setup.Coordinator.BeginForegroundEditing().Success,
            "전경 수정 모드 전환에 실패했습니다.");
        setup.Native.Bounds[setup.Native.Calendar] =
            new ScreenWindowBounds(-430, 610, 820, 740);

        WindowTransitionResult cancelled =
            setup.Coordinator.TryCommitDesktop(original.ToPixelRect());

        Assert(cancelled.Success && cancelled.State.IsValid,
            "취소 전환이 모든 불변식을 만족하지 못했습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == original,
            "취소 후 원래 Bounds가 정확히 복원되지 않았습니다.");
    }

    private static void ExplorerRestartReattaches()
    {
        var setup = CreateCoordinator();
        Assert(setup.Coordinator.InitializeDesktop().Success,
            "초기 데스크톱 연결에 실패했습니다.");
        ScreenWindowBounds expected = setup.Native.Bounds[setup.Native.Calendar];
        setup.Native.ActiveHost = setup.Native.RestartedHost;
        setup.Native.ActiveIconView = setup.Native.RestartedIconView;
        setup.Native.Parents[setup.Native.RestartedIconView] =
            setup.Native.RestartedHost;

        DesktopWindowState repaired = setup.Coordinator.MaintainDesktopState();

        Assert(repaired.IsValid, "Explorer 재시작 후 불변식 복구에 실패했습니다.");
        Assert(setup.Native.Parents[setup.Native.Calendar] ==
               setup.Native.RestartedHost,
            "달력이 새 Explorer 호스트에 연결되지 않았습니다.");
        Assert(setup.Native.Bounds[setup.Native.Calendar] == expected,
            "Explorer 재부착 중 Bounds가 변경되었습니다.");
        Assert(setup.Native.NextWindows[setup.Native.RestartedIconView] ==
               setup.Native.Calendar,
            "새 아이콘 뷰가 달력 위에 배치되지 않았습니다.");
    }

    private static void DesktopSurfaceTargetsOnly()
    {
        var native = new FakeWindowNativeApi();
        var tester = new DesktopSurfaceHitTester(native,
            new DesktopShellLocator(native));
        IntPtr desktopChild = new(35);
        IntPtr otherWindow = new(60);
        IntPtr otherChild = new(61);
        native.Parents[desktopChild] = native.IconView;
        native.Parents[native.Calendar] = native.Host;
        native.Parents[otherChild] = otherWindow;

        Assert(tester.IsDesktopSurface(native.Host),
            "바탕화면 호스트를 거부했습니다.");
        Assert(tester.IsDesktopSurface(native.IconView),
            "바탕화면 뷰를 거부했습니다.");
        Assert(tester.IsDesktopSurface(desktopChild),
            "바탕화면 뷰의 자식 창을 거부했습니다.");
        Assert(!tester.IsDesktopSurface(native.Calendar),
            "달력 창을 바탕화면으로 잘못 판정했습니다.");
        Assert(!tester.IsDesktopSurface(otherWindow) &&
               !tester.IsDesktopSurface(otherChild),
            "다른 앱 창을 바탕화면으로 잘못 판정했습니다.");
        Assert(!tester.IsDesktopSurface(IntPtr.Zero),
            "빈 창 핸들을 허용했습니다.");
        native.InvalidWindows.Add(desktopChild);
        Assert(!tester.IsDesktopSurface(desktopChild),
            "유효하지 않은 창 핸들을 허용했습니다.");

        native.ActiveIconView = IntPtr.Zero;
        Assert(!tester.IsDesktopSurface(native.Host),
            "Explorer 뷰를 찾지 못했는데 클릭을 허용했습니다.");
    }

    private static void DoubleClickRequiresSameTarget()
    {
        var native = new FakeWindowNativeApi();
        using var monitor = new GlobalPointerMonitor(native);
        int doubleClicks = 0;
        monitor.DoubleClicked += (_, _) => doubleClicks++;

        monitor.ProcessLeftButtonDown(new GlobalPointerMonitor.ScreenPoint(
            100, 100, new IntPtr(60)), 100);
        monitor.ProcessLeftButtonDown(new GlobalPointerMonitor.ScreenPoint(
            100, 100, native.IconView), 110);
        Assert(doubleClicks == 0,
            "서로 다른 대상 창의 클릭을 더블클릭으로 판정했습니다.");

        monitor.Stop();
        monitor.ProcessLeftButtonDown(new GlobalPointerMonitor.ScreenPoint(
            100, 100, native.IconView), 100);
        monitor.ProcessLeftButtonDown(new GlobalPointerMonitor.ScreenPoint(
            100, 100, native.IconView), 110);
        Assert(doubleClicks == 1,
            "동일한 대상 창의 정상 더블클릭을 감지하지 못했습니다.");
    }

    private static (DesktopCalendarWindowCoordinator Coordinator,
        FakeWindowNativeApi Native) CreateCoordinator()
    {
        var native = new FakeWindowNativeApi();
        var locator = new DesktopShellLocator(native);
        var bounds = new WindowBoundsService(native);
        var layer = new DesktopLayerService(native);
        var attachment = new DesktopAttachmentService(native);
        var initial = new ScreenWindowBounds(-880, 337, 1080, 969);
        var coordinator = new DesktopCalendarWindowCoordinator(
            () => native.Calendar, initial, 700, 480, native, locator,
            bounds, layer, attachment);
        return (coordinator, native);
    }

    private static void PixelRectLike(Avalonia.PixelRect actual,
        ScreenWindowBounds expected) => Assert(actual.X == expected.X &&
        actual.Y == expected.Y && actual.Width == expected.Width &&
        actual.Height == expected.Height, "캡처 Bounds가 일치하지 않습니다.");

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class FakeAccountSession : IAccountSession
{
    public UserInfo? User { get; } = new(Guid.NewGuid(), "user@example.com");
    public Exception? ChangeError { get; init; }
    public Exception? DeleteError { get; init; }
    public string? ChangedTo { get; private set; }
    public string? DeletedWith { get; private set; }

    public Task ChangePasswordAsync(string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (ChangeError is not null) return Task.FromException(ChangeError);
        ChangedTo = newPassword;
        return Task.CompletedTask;
    }

    public Task DeleteAccountAsync(string password,
        CancellationToken cancellationToken = default)
    {
        if (DeleteError is not null) return Task.FromException(DeleteError);
        DeletedWith = password;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryCalendarRepository : ICalendarRepository
{
    private readonly List<CalendarItem> _items = [];
    private readonly List<DateCellDecoration> _decorations = [];
    private SyncState _syncState = new(0);
    public event EventHandler? Changed;
    public int UpsertCount { get; private set; }
    public int DeleteCount { get; private set; }
    public int OccurrenceQueryCount { get; private set; }

    public Task<IReadOnlyList<CalendarItem>> GetAllItemsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CalendarItem>>(_items
            .Where(item => !item.IsDeleted).ToArray());

    public Task<IReadOnlyList<CalendarItem>> GetItemsByRangeAsync(DateOnly from,
        DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CalendarItem>>(_items.Where(item =>
            !item.IsDeleted && item.StartDate <= to &&
            (item.Recurrence is not null || item.EndDate >= from)).ToArray());

    public Task<IReadOnlyList<CalendarOccurrence>> GetOccurrencesByRangeAsync(
        DateOnly from,
        DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult(GetOccurrences(from, to));

    private IReadOnlyList<CalendarOccurrence> GetOccurrences(DateOnly from,
        DateOnly to)
    {
        OccurrenceQueryCount++;
        return CalendarOccurrenceEngine.GetOccurrences(_items, from, to);
    }

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<IReadOnlyList<DateCellDecoration>> GetDecorationsByRangeAsync(
        DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DateCellDecoration>>(_decorations
            .Where(item => !item.IsDeleted && item.Date >= from &&
                item.Date <= to).ToArray());

    public Task UpsertItemAsync(CalendarItem item,
        CancellationToken cancellationToken = default)
    {
        int index = _items.FindIndex(current => current.Id == item.Id);
        if (index < 0) _items.Add(item);
        else _items[index] = item;
        UpsertCount++;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task DeleteItemAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => item.Id == id);
        DeleteCount++;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task UpsertDecorationAsync(DateCellDecoration item,
        CancellationToken cancellationToken = default)
    {
        int index = _decorations.FindIndex(current => current.Id == item.Id);
        if (index < 0) _decorations.Add(item);
        else _decorations[index] = item;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task DeleteDecorationAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        _decorations.RemoveAll(item => item.Id == id);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<SyncState> GetSyncStateAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_syncState);

    public Task SetSyncStateAsync(SyncState state,
        CancellationToken cancellationToken = default)
    {
        _syncState = state;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}

internal readonly record struct NativePositionCall(IntPtr Window,
    IntPtr InsertAfter, int X, int Y, int Width, int Height,
    WindowPositionFlags Flags);

internal sealed class FakeWindowNativeApi : IWindowNativeApi
{
    public IntPtr Calendar { get; } = new(10);
    public IntPtr Host { get; } = new(20);
    public IntPtr IconView { get; } = new(30);
    public IntPtr Progman { get; } = new(40);
    public IntPtr RestartedHost { get; } = new(21);
    public IntPtr RestartedIconView { get; } = new(31);
    public IntPtr ActiveHost { get; set; }
    public IntPtr ActiveIconView { get; set; }
    public Dictionary<IntPtr, IntPtr> Parents { get; } = [];
    public Dictionary<IntPtr, IntPtr> NextWindows { get; } = [];
    public Dictionary<IntPtr, ScreenWindowBounds> Bounds { get; } = [];
    public List<NativePositionCall> PositionCalls { get; } = [];
    public HashSet<IntPtr> InvalidWindows { get; } = [];
    public bool FailLayerChange { get; set; }
    public IntPtr WindowAtPoint { get; set; }
    private readonly Dictionary<IntPtr, long> _styles = [];
    private readonly Dictionary<IntPtr, long> _extendedStyles = [];
    private readonly IntPtr _monitor = new(50);
    private const int HostOriginX = -1080;
    private const int HostOriginY = -383;

    public FakeWindowNativeApi()
    {
        ActiveHost = Host;
        ActiveIconView = IconView;
        Parents[Calendar] = IntPtr.Zero;
        Parents[IconView] = Host;
        Bounds[Calendar] = new ScreenWindowBounds(-880, 337, 1080, 969);
    }

    public IntPtr FindTopLevelWindow(string className) =>
        className == "Progman" ? Progman : IntPtr.Zero;
    public IntPtr FindChildWindow(IntPtr parent, string className,
        string? windowName = null) =>
        parent == ActiveHost && className == "SHELLDLL_DefView"
            ? ActiveIconView : IntPtr.Zero;
    public IReadOnlyList<IntPtr> EnumerateTopLevelWindows() =>
        [ActiveHost, Progman];
    public void RequestDesktopWorkerWindow(IntPtr progman) { }
    public IntPtr WindowFromPoint(int screenX, int screenY) => WindowAtPoint;
    public bool IsWindow(IntPtr window) => window != IntPtr.Zero &&
        !InvalidWindows.Contains(window);
    public IntPtr GetParent(IntPtr window) =>
        Parents.TryGetValue(window, out IntPtr parent) ? parent : IntPtr.Zero;
    public IntPtr GetNextWindow(IntPtr window) =>
        NextWindows.TryGetValue(window, out IntPtr next) ? next : IntPtr.Zero;
    public bool TrySetParent(IntPtr child, IntPtr parent, out int error)
    {
        Parents[child] = parent;
        error = 0;
        return true;
    }
    public long GetStyle(IntPtr window) => _styles.GetValueOrDefault(window);
    public long GetExtendedStyle(IntPtr window) =>
        _extendedStyles.GetValueOrDefault(window);
    public void SetStyle(IntPtr window, long style) => _styles[window] = style;
    public void SetExtendedStyle(IntPtr window, long style) =>
        _extendedStyles[window] = style;

    public bool SetWindowPosition(IntPtr window, IntPtr insertAfter, int x,
        int y, int width, int height, WindowPositionFlags flags,
        out int error)
    {
        PositionCalls.Add(new NativePositionCall(window, insertAfter, x, y,
            width, height, flags));
        if (FailLayerChange && insertAfter == ActiveIconView &&
            flags.HasFlag(WindowPositionFlags.NoMove) &&
            flags.HasFlag(WindowPositionFlags.NoSize))
        {
            error = 5;
            return false;
        }
        if (!flags.HasFlag(WindowPositionFlags.NoMove) ||
            !flags.HasFlag(WindowPositionFlags.NoSize))
        {
            ScreenWindowBounds current = Bounds.GetValueOrDefault(window);
            int screenX = x;
            int screenY = y;
            if (GetParent(window) == ActiveHost)
            {
                screenX += HostOriginX;
                screenY += HostOriginY;
            }
            Bounds[window] = new ScreenWindowBounds(
                flags.HasFlag(WindowPositionFlags.NoMove) ? current.X : screenX,
                flags.HasFlag(WindowPositionFlags.NoMove) ? current.Y : screenY,
                flags.HasFlag(WindowPositionFlags.NoSize) ? current.Width : width,
                flags.HasFlag(WindowPositionFlags.NoSize) ? current.Height : height);
        }
        if (!flags.HasFlag(WindowPositionFlags.NoZOrder) &&
            insertAfter == ActiveIconView)
        {
            NextWindows[ActiveIconView] = Calendar;
            NextWindows[Calendar] = IntPtr.Zero;
        }
        error = 0;
        return true;
    }

    public bool TryGetWindowRect(IntPtr window, out NativeWindowRect bounds)
    {
        if (!Bounds.TryGetValue(window, out ScreenWindowBounds value))
        {
            bounds = default;
            return false;
        }
        bounds = value.ToNativeRect();
        return true;
    }

    public bool TryScreenToClient(IntPtr window, int screenX, int screenY,
        out int clientX, out int clientY)
    {
        clientX = screenX - HostOriginX;
        clientY = screenY - HostOriginY;
        return window == ActiveHost;
    }
    public bool ShowWindow(IntPtr window) => true;
    public IntPtr MonitorFromRect(NativeWindowRect bounds, bool nearest)
    {
        bool intersects = bounds.Right > -1080 && bounds.Left < 0 &&
            bounds.Bottom > -383 && bounds.Top < 1537;
        return intersects || nearest ? _monitor : IntPtr.Zero;
    }
    public bool TryGetMonitorWorkArea(IntPtr monitor,
        out NativeMonitorWorkArea workArea)
    {
        workArea = new NativeMonitorWorkArea(-1080, -383, 0, 1489);
        return monitor == _monitor;
    }
}
