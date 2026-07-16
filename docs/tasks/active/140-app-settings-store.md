# 140 앱 설정 저장소 확장

## 상태

- 상태: 활성
- 브랜치: `refactor/app-settings-store`

## 목표

창 위치·크기만 저장하는 `Services/CalendarSettingsStore.cs`를 여러 설정을
담을 수 있는 확장 가능한 저장소로 일반화한다. 이후 설정 창(150), 표시
옵션(160·170), 서버 주소(250) 등 다수 작업의 공통 기반이다.

## 의존성

- 없음.

## 범위

- `AppSettings` 레코드를 도입한다. 기존 `X/Y/Width/Height` 필드를 그대로
  포함하고, 신규 필드는 전부 nullable 또는 기본값으로 추가해 구버전 파일과
  하위 호환을 유지한다.
- 기존 `%LocalAppData%\HmDesktopCalendar\settings.json` 경로와 파일을
  유지하고 스키마만 확장한다.
- 원자적 저장(임시 파일에 쓴 뒤 이동으로 교체)을 적용한다.
- 역직렬화 실패 시 기본값 폴백(기존 동작)을 유지한다.
- 설정 변경 알림 이벤트를 제공한다(설정 창이 저장하면 앱이 반영).
- `App.axaml.cs`의 기존 호출부는 최소 수정으로 유지한다.

## 제외 범위

- 설정 UI(작업 150).
- 개별 설정 항목의 실제 동작(160, 170, 180, 250).
- 설정 파일 위치 이동이나 형식 변경.

## 설계 결정

- 신규 파일·마이그레이션 없이 같은 settings.json을 관대한 역직렬화로
  확장한다. 알 수 없는 필드는 무시하고 누락 필드는 기본값을 쓴다.
- `SchemaVersion`은 기본값 1인 첫 확장 필드로 두고, 현재는 버전에 따른
  마이그레이션 분기를 만들지 않는다.
- 저장소는 단일 인스턴스로 `App`이 소유하고 필요한 창·서비스에 주입한다.

## 완료 조건

- 창 위치만 있는 기존 settings.json을 그대로 읽는다.
- 저장 후에도 창 위치 저장·복원 동작이 기존과 동일하다.
- 신규 필드가 저장·로드 왕복에서 유지된다.

## 검증

- 구버전 JSON 로드, `SchemaVersion` 왕복 직렬화, 변경 알림, 기존
  `PixelRect` 저장 시 확장 필드 보존, 원자 저장 임시 파일 정리와 손상 파일
  폴백 회귀 테스트를 추가했다.
- `dotnet build tests/HmDesktopCalendar.RegressionTests/HmDesktopCalendar.RegressionTests.csproj --no-restore`
  성공, 경고 0개·오류 0개.
- `dotnet run --project tests/HmDesktopCalendar.RegressionTests/HmDesktopCalendar.RegressionTests.csproj --no-build`
  회귀 테스트 55개 통과.

## 작업 결과

- 커밋: 미정
- PR: 미정
