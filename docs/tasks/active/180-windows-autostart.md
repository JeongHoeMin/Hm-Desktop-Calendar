# 180 Windows 시작 시 자동 실행

## 상태

- 상태: 활성
- 브랜치: `feat/windows-autostart`

## 목표

바탕화면 상주 앱답게 Windows 로그인 시 자동 실행되도록 설정 창 토글을
제공한다. 현재는 매번 수동 실행해야 하는 수명주기 결함이 있다.

## 의존성

- 150 (설정 창 배치)

## 범위

- `DesktopIntegration/AutoStartRegistrar`(신규):
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`에
  `HmDesktopCalendar` 값을 등록/해제한다. 경로는
  `Environment.ProcessPath`를 인용 부호로 감싸 등록한다.
- 설정 창에 자동 실행 토글을 추가한다.
- 레지스트리 접근 실패 시 토글을 비활성화하고 사유를 표시한다.

## 제외 범위

- HKLM 기반 전체 사용자 등록(관리자 권한 필요).
- 시작 시 최소화 옵션(이미 트레이 상주형).
- 설치 프로그램.

## 설계 결정

- 토글 상태는 settings.json에 저장하지 않고 레지스트리에서 직접 읽는다.
  레지스트리가 단일 진실 원천이며, 사용자가 작업 관리자에서 끈 경우의
  불일치를 방지한다.
- `Microsoft.Win32.Registry`를 직접 사용한다(Windows 전용 앱이므로 허용).
- `dotnet run` 개발 실행 시에는 dotnet.exe 경로가 등록되는 한계가 있다.
  배포 빌드 기준 기능임을 명시한다.
- 비-Windows 환경이나 실행 파일 경로·레지스트리 접근이 불가능한 경우 토글을
  비활성화하고 `AutoStartStatus`의 오류 사유를 표시한다.

## 완료 조건

- 토글 켬 → 재로그인 시 앱이 자동 실행된다.
- 토글 끔 → Run 키 값이 삭제된다.
- 작업 관리자에서 비활성화해도 토글 표시가 실제 상태와 일치한다.

## 검증

- 레지스트리 값 쓰기/삭제 왕복 회귀 테스트(테스트 전용 값 이름 사용 후
  정리).
- 재로그인 자동 실행 수동 확인.

### 실행 결과

- `dotnet build HmDesktopCalendar.csproj -c Debug`: 성공(경고 0, 오류 0).
- `dotnet run --project tests/HmDesktopCalendar.RegressionTests/HmDesktopCalendar.RegressionTests.csproj -c Debug`:
  63개 회귀 테스트 성공.
- 테스트 전용 Run 값 이름으로 인용된 실행 경로 쓰기·읽기·삭제를 왕복 검증하고
  `finally`에서 정리했다.
- Windows UI 자동화로 설정 창 하단의 자동 시작 토글, 설명과 접근성 요소를
  확인했다.
- 스크린샷: `docs/screenshots/180/180-windows-autostart.png`.
- 현재 사용자 세션을 종료해야 하는 재로그인 자동 실행 확인은 수행하지 않았다.
  실제 배포 실행 파일 기준 수동 확인 항목으로 PR에 기록한다.

## 작업 결과

- 커밋: 미정
- PR: 미정
