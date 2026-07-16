# HmDesktopCalendar Server

## 준비

1. `.env.example`을 `.env`로 복사하고 PostgreSQL 연결 정보와 두 개의 충분히 긴 비밀키를 입력합니다.
2. `pnpm install` 또는 `npm install`을 실행합니다.
3. `pnpm migrate`로 스키마를 생성합니다.
4. `pnpm dev`로 개발 서버를 실행합니다.

기본 주소는 `http://127.0.0.1:3000`입니다. 클라이언트가 다른 서버를 사용해야 하면 실행 전에
`HM_CALENDAR_SERVER_URL` 환경변수를 설정합니다.

## 명령

- `pnpm build`: TypeScript 빌드
- `pnpm test`: DB 비의존 단위 테스트
- `pnpm migrate`: PostgreSQL migration
- `pnpm start`: 빌드된 서버 실행

통합 테스트와 실제 실행에는 `.env`의 PostgreSQL 인스턴스가 필요합니다.

## 비밀 정보 관리

- `.env`와 실제 데이터베이스 접속 정보, JWT 비밀키는 커밋하지 않습니다. 저장소에는
  placeholder만 포함한 `.env.example`만 유지합니다.
- 운영 환경에서는 `.env.example`의 `change-me`, `postgres:postgres`,
  `replace-with` 값을 그대로 사용할 수 없으며 서버가 시작 전에 실패합니다.
- 비밀 정보가 로그, 이슈 또는 Git 이력에 노출되면 해당 데이터베이스 비밀번호와
  비밀키를 즉시 회전하고, 기존 자격 증명을 폐기한 뒤 접근 로그와 외부 접근 범위를
  점검합니다. 공유 Git 이력에서 문자열만 삭제하는 것으로 회전을 대신하지 않습니다.

## API 호환성

- `/v1` 인증·할 일·동기화 API는 기존 클라이언트를 위해 유지한다.
- `/v2/calendar-items`와 `/v2/date-cell-decorations`는 일정과 날짜 셀 장식을
  저장한다.
- `/v2/sync`는 두 엔터티의 변경을 하나의 증가 커서로 반환한다.
- 시작할 때 적용되지 않은 `src/database`의 SQL 마이그레이션을 파일명 순서대로
  트랜잭션 안에서 실행한다.
