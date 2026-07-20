# 250 서버 주소 설정 UI

## 상태

- 상태: 완료
- 브랜치: `feat/server-url-setting`

## 목표

환경 변수 없이 설정 창에서 동기화 서버 주소를 지정할 수 있게 하고, 코드에
산재한 하드코딩 기본 주소를 단일 지점으로 정리한다.

## 의존성

- 140 (설정 저장소), 150 (설정 창 배치)

## 범위

- `AppSettings`에 `ServerUrl`을 추가한다. 적용 우선순위:
  `HM_CALENDAR_SERVER_URL` 환경 변수 > settings.json > 기본
  `http://127.0.0.1:3000`.
- `ServerEndpoint` 공급자를 도입해 `App.axaml.cs`의 조립과 각 클라이언트
  생성자 기본값 하드코딩(`AuthSession`, `RemoteTodoRepository`,
  `RemoteCalendarRepository`, `RealtimeSyncClient`)을 공급자 참조로
  통일한다. ws URL 파생(http→ws)도 공급자로 옮긴다.
- 설정 창 "동기화" 섹션: URL 입력, http/https 형식 검증, "재시작 후 적용"
  안내, 주소 변경 저장 시 로그아웃 경고(주소가 바뀌면 기존 세션이 무효).

## 제외 범위

- 무중단 런타임 재연결. `AuthSession`·원격 리포지토리·실시간 클라이언트
  재구성 복잡도가 커서 재시작 적용으로 단순화한다.
- 서버 연결 상태 표시등.

## 설계 결정

- 잘못된 형식의 URL은 저장을 거부하고 인라인 오류를 표시한다.
- 환경 변수가 설정돼 있으면 UI 입력보다 우선하며, 이 사실을 설정 창에
  표시한다.
- 잘못된 환경 변수는 로컬 기본 주소로 조용히 우회하지 않고 시작 시 명확한 오류로
  거부한다.

## 완료 조건

- 설정한 주소가 재시작 후 로그인, 동기화, 실시간 연결 전부에 적용된다.
- 환경 변수가 있으면 환경 변수가 우선한다.

## 검증

- 회귀 테스트: 우선순위 결정, URL 형식 검증, ws URL 파생.
- 로컬 서버 주소를 바꿔 재시작 후 동작을 수동 확인한다.

### 현재 검증 결과

- `dotnet build HmDesktopCalendar.csproj -c Debug`: 경고·오류 없이 성공.
- 전체 회귀 테스트 77개 통과. 환경 변수·설정·기본값 우선순위, URL 형식 검증,
  HTTP→WebSocket 주소 파생과 설정 화면 저장·복원을 포함한다.
- Windows 설정 창을 480×480 최소 크기로 열어 URL 입력, 환경 변수 우선 안내,
  로그인 세션 경고, 저장 버튼과 세로 스크롤 접근성을 확인했다.
- `https://manual.example.test/restart`를 저장하고 설정 창을 다시 생성해 값이
  복원되고 저장 버튼이 비활성화되는 것을 확인했다.
- 스크린샷: [`docs/screenshots/250-server-url-setting.png`](../../screenshots/250-server-url-setting.png)

## 작업 결과

- 커밋: `8e5995f9635d930850b4b7eb157a440e96982f1b`
- PR: https://github.com/JeongHoeMin/Hm-Desktop-Calendar/pull/30
