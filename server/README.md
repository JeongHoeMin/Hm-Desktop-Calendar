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

Docker Compose를 이용한 운영 배포, TLS 리버스 프록시, API 스모크 확인, PostgreSQL
백업·복원 절차는 [`docs/deployment.md`](docs/deployment.md)를 따른다.

## 계정 관리 API

- `POST /v1/auth/password`는 액세스 토큰과 `currentPassword`, `newPassword`를 받아
  비밀번호를 변경하고 사용자의 모든 갱신 토큰을 폐기한다. 이미 발급된 액세스
  토큰은 남은 유효 시간 동안 사용할 수 있으므로 성공한 클라이언트는 로컬 세션을
  지우고 다시 로그인해야 한다.
- `DELETE /v1/auth/me`는 액세스 토큰과 `password`를 검증한 뒤 사용자와 FK cascade로
  연결된 모든 서버 데이터를 물리 삭제하고 `204 No Content`를 반환한다.
- 두 API는 로그인·가입 API와 같은 강화 요청 한도를 사용하며 비밀번호 불일치는
  `401`과 `{ "message": "현재 비밀번호가 올바르지 않습니다." }`를 반환한다.

## 서버 보안 설정

- 모든 응답에 `@fastify/helmet`의 기본 보안 헤더를 적용한다.
- IP별 전역 요청 한도는 기본 1분에 300회이며, 회원 가입과 로그인은 각 경로별로
  1분에 10회다. `RATE_LIMIT_MAX`, `AUTH_RATE_LIMIT_MAX`,
  `RATE_LIMIT_WINDOW_MS`로 양의 정수 값을 조정할 수 있다.
- rate-limit 상태는 프로세스 메모리에만 저장하므로 현재 배포 모델은 서버 단일
  인스턴스를 전제로 한다. 여러 인스턴스로 확장하려면 공유 저장소 기반 limiter가
  필요하다.
- 요청 본문은 기본 262,144바이트로 제한한다. `BODY_LIMIT_BYTES`로 양의 정수 값을
  지정할 수 있다.
- CORS는 기본적으로 비활성이다. 브라우저 클라이언트가 필요한 경우에만
  `CORS_ALLOWED_ORIGINS=https://calendar.example.com,http://localhost:5173`처럼
  정확한 HTTP(S) origin을 쉼표로 구분해 지정한다. `*` 와 경로가 포함된 URL은
  허용하지 않는다. 데스크톱 클라이언트에는 이 설정이 필요하지 않다.
- 요청 로그의 `authorization` 헤더 값은 Pino에서 `[Redacted]`로 마스킹한다.

기본값 예시는 `.env.example`에 있다. 환경 변수 값이 유효하지 않으면 서버가
시작 전에 실패한다.

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
