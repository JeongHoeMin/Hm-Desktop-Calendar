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
