# 200 ICS 내보내기

## 상태

- 상태: 활성
- 브랜치: `feat/ics-export`

## 목표

전체 일정을 표준 iCalendar(.ics) 파일로 내보내 다른 캘린더 앱으로의 이전과
보관을 지원한다. 데이터 소유권 확보(안전망)의 일부다.

## 의존성

- 없음. 진입점이 일정 모아보기 창이므로 설정 창(150)과 무관하다.

## 범위

- `Services/IcsExporter`(신규): `CalendarItem` → VEVENT 매핑.
  - 반복 규칙은 발생 엔진 규칙을 RRULE로 직렬화하고, 매핑할 수 없는 규칙은
    향후 2년 발생 전개로 폴백한다.
  - 종일 일정은 `DTSTART;VALUE=DATE`, 기간 일정은 DTEND(exclusive) 규칙을
    준수한다.
  - 텍스트 이스케이프와 75옥텟 line folding을 적용한다.
  - UID는 `CalendarItem` ID 기반으로 고정 생성해 재내보내기 시 동일하다.
  - 시간대는 Asia/Seoul 고정.
- 일정 모아보기 창에 "ICS 내보내기" 버튼을 추가하고 Avalonia
  `StorageProvider` 저장 대화상자로 파일 위치를 받는다.

## 제외 범위

- ICS 가져오기. 중복 병합과 ID 충돌 처리가 복잡해 별도 후속 작업 후보로
  남긴다.
- 구독 URL 발행.
- 날짜 셀 장식(배경색) 데이터. 일정과 기념일만 내보낸다(기념일은 연간 반복
  VEVENT로 표현).

## 설계 결정

- 내보내기 범위는 저장소의 전체 시리즈 원본이다(모아보기의 현재 필터 결과가
  아님). 필터 결과 내보내기가 필요해지면 별도 작업으로 등록한다.
- 내보내기 실패(파일 쓰기 오류)는 한국어 오류 메시지로 표시하고 부분
  파일을 남기지 않는다.
- 현재 발생 엔진이 허용하는 일·주·월·연 반복은 모두 RRULE로 직렬화한다. 향후
  발생 엔진에 RRULE로 표현할 수 없는 규칙이 추가되면 내보내기 시점부터 2년간의
  발생 항목을 고정 UID 접미사와 함께 개별 VEVENT로 전개한다.
- `Asia/Seoul`의 KST(+09:00) VTIMEZONE을 파일에 포함해 외부 앱이 TZID를 독립적으로
  해석할 수 있게 한다.

## 완료 조건

- 내보낸 파일을 Google Calendar 또는 Outlook에서 가져오기에 성공한다.
- 반복·기간 일정의 발생이 앱 표시와 일치한다.

## 검증

- 회귀 테스트: VEVENT 직렬화 스냅샷, folding·이스케이프, 반복 규칙 → RRULE
  변환.
- 외부 캘린더 앱 가져오기 수동 확인을 PR에 기록한다.

### 실행 결과

- `dotnet build HmDesktopCalendar.csproj -c Release`: 성공(경고 0, 오류 0).
- `dotnet run --project tests/HmDesktopCalendar.RegressionTests/HmDesktopCalendar.RegressionTests.csproj -c Release`:
  70개 통과.
- Outlook 일정 열기에서 생성한 ICS를 열어 제목과 행사 창 생성까지 확인했다. 실제
  사용자 캘린더에는 저장하지 않았다.
- 일정 모아보기 창을 920×680에서 직접 확인하고
  `docs/screenshots/200/200-ics-export.png`에 기록했다. 버튼은 UI Automation 트리에서
  `ICS 내보내기`로 노출된다.

## 작업 결과

- 커밋: PR 생성 후 기록
- PR: 생성 후 기록
