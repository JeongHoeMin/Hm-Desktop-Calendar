# 130 한국 공휴일 표시

## 상태

- 상태: 활성
- 브랜치: `feat/korean-holidays`

## 목표

달력 날짜 셀에 한국 법정 공휴일을 표시해 사용자가 별도 조회 없이 휴일을
인지하게 한다. 네트워크·저장소·동기화 변경 없이 로컬 계산만으로 동작한다.

## 의존성

- 없음.

## 범위

- `Calendar/KoreanHolidayCalculator.cs`(신규): 순수 정적 계산기.
  - `KoreanHoliday(DateOnly Date, string Name, bool IsSubstitute)` 레코드와
    `DisplayName`(대체 시 `대체공휴일(이름)`).
  - `GetHolidays(int year)`: 지원 범위(1900~2050) 밖이면 빈 목록 반환(예외
    금지). 연도별 캐시는 `ConcurrentDictionary`(RefreshAsync가 UI 스레드
    밖에서 실행될 수 있음).
  - `GetHolidayNames(DateOnly from, DateOnly to)`: 날짜 → 셀 표시 이름.
    같은 날 공휴일이 겹치면 이름을 `·`로 결합(예: 2025-05-05
    "어린이날·부처님오신날").
- 고정 양력: 신정 1/1, 삼일절 3/1, 노동절 5/1, 어린이날 5/5, 현충일 6/6,
  제헌절 7/17, 광복절 8/15, 개천절 10/3, 한글날 10/9, 성탄절 12/25.
- 음력: `System.Globalization.KoreanLunisolarCalendar`로 설날(음력 1/1
  ±1일), 추석(음력 8/15 ±1일), 부처님오신날(음력 4/8) 계산. 윤달 보정 필수:
  `GetLeapMonth` 반환값이 0이 아니고 대상 월이 그 이상이면 월 인덱스 +1.
- 대체공휴일(현행 규칙): 설·추석 연휴는 일요일 또는 다른 공휴일과 겹치면,
  국경일(삼일절·제헌절·광복절·개천절·한글날)·노동절·어린이날·
  부처님오신날·성탄절은
  토/일 또는 다른 공휴일과 겹치면 "다음 날부터 토·일도 아니고 기존/대체
  공휴일도 아닌 첫날"로 대체. 신정·현충일은 대체 없음.
- `ViewModels/CalendarViewModel.cs` `RefreshAsync`에서
  `GetHolidayNames(dates[0], dates[^1])`을 조회해 **새 그리드 생성 경로와
  같은 그리드 `Update` 재사용 경로 모두**에 전달한다.
- `ViewModels/CalendarDayViewModel.cs`: 생성자와 `Update()`에
  `string? holidayName = null` 선택 매개변수 추가(기존 호출부 호환).
  `IsHoliday`, `HolidayName`, `DayForegroundBrush` 속성 추가와 변경 알림.
- `Views/MainWindow.axaml` 셀 헤더: 공휴일 이름 배지(배경 `#FFE9EC`, 글자
  `#C8102E`, `TextTrimming="CharacterEllipsis"`) 추가, 날짜 숫자 Foreground를
  `DayForegroundBrush`로 교체(공휴일 빨강은 `#FF5065` 계열).

## 제외 범위

- 일정 모아보기 창 노출(필요 시 후속 작업으로 등록).
- 임시공휴일·선거일(정부 지정 데이터 소스 필요).
- 주말 날짜 숫자 색(작업 160).

## 설계 결정

- 외부 API·데이터 파일 없이 로컬 계산한다. 저장소·동기화·서버를 변경하지
  않는다(파생 데이터).
- 합성 위치는 리포지토리가 아닌 `CalendarViewModel` 레벨이다.
  `ICalendarRepository` 계약과 3개 구현을 건드리지 않는다.
- 숫자 색 우선순위: 사용자 배경색 밝기 기반 전경(`CalendarCellColor`) >
  공휴일 빨강 > 테마 기본. 사용자 배경색 셀에서 공휴일 식별은 자체 배경을
  가진 배지가 담당한다.
- 대체공휴일은 현행 규정을 전 지원 연도에 동일 적용한다. 과거 연도는
  역사적 사실과 다를 수 있음을 수용한다(규칙 이력 테이블 없음). 규칙 출처는
  2026년 5월 11일 시행 「관공서의 공휴일에 관한 규정」(대통령령 제36290호)
  제2조·제3조이며 개정 시 계산기만 수정한다.
  - https://law.go.kr/lsInfoP.do?ancYnChk=0&lsId=002404
- 배지는 일정 줄이 아닌 셀 헤더에 둔다. 일정 줄에 넣으면 셀 용량을 소비해
  실제 일정이 밀려나기 때문이다.

## 완료 조건

- 2025~2026년 전 공휴일(대체공휴일 포함)이 정확히 표시된다.
- 지원 범위 밖 연도로 이동해도 표시 없이 정상 동작한다.
- 사용자 배경색이 설정된 공휴일 셀에서 가독성이 유지된다.

## 검증

- 2025·2026년 전체 공휴일 집합, 2027년 노동절·제헌절 대체공휴일,
  1899·2051년 범위 밖 결과, 같은 날 이름 결합을 회귀 테스트로 검증했다.
- 2025년 10월 새 그리드와 `Update` 재사용 경로, 사용자 배경색 전경 우선순위,
  공휴일 빨간 전경과 2051년 이동 후 표시 제거를 회귀 테스트로 검증했다.
- `dotnet build tests/HmDesktopCalendar.RegressionTests/HmDesktopCalendar.RegressionTests.csproj --no-restore`
  성공, 경고 0개·오류 0개.
- `dotnet run --project tests/HmDesktopCalendar.RegressionTests/HmDesktopCalendar.RegressionTests.csproj --no-build`
  회귀 테스트 52개 통과.
- 700×480 최소 크기의 실제 XAML 화면에서 2025년 1월·5월·10월과 2051년 1월을
  검토했다. 긴 이름 말줄임, 연휴 배지, 일반 날짜 전경, 범위 밖 표시 제거가
  정상 동작했다.
- 스크린샷: [5월 겹친 공휴일](../../screenshots/130/130-korean-holidays-may.png),
  [10월 연휴와 대체공휴일](../../screenshots/130/130-korean-holidays-minimum.png),
  [2051년 범위 밖](../../screenshots/130/130-korean-holidays-out-of-range.png)

## 작업 결과

- 커밋: 미정
- PR: 미정
