# 로컬 백업 복원 절차

Hm Desktop Calendar는 `%LocalAppData%\HmDesktopCalendar\backups` 아래에 하루 한 번
일정 데이터를 보관하고 최근 10개 세대를 유지한다. `settings.json`은 백업하지
않으며 각 세대에는 루트 일정 JSON과 `accounts` 계정별 데이터가 상대 경로 그대로
들어 있다.

## 복원

1. 트레이 메뉴에서 앱을 종료하고 작업 관리자에서도 `HmDesktopCalendar` 프로세스가
   없는지 확인한다.
2. 설정 창의 **백업 폴더 열기**에서 복원할 `yyyyMMdd-HHmmss` 폴더를 선택한다.
3. 현재 `%LocalAppData%\HmDesktopCalendar` 폴더를 별도 위치에 복사해 복원 전 상태를
   보존한다.
4. 선택한 백업 폴더의 파일과 `accounts` 폴더를
   `%LocalAppData%\HmDesktopCalendar`에 같은 상대 경로로 복사하고 기존 파일을
   교체한다. `backups` 폴더 자체나 `settings.json`은 교체하지 않는다.
5. 앱을 다시 실행해 익명 범위와 로그인 계정 범위의 일정이 정상적으로 열리는지
   확인한다.

복원 후 문제가 있으면 앱을 다시 종료하고 3단계에서 보관한 원본을 같은 방식으로
되돌린다. 실행 중 파일을 교체하면 저장 작업과 충돌할 수 있으므로 반드시 앱을 먼저
종료한다.
