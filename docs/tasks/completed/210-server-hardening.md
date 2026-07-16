# 210 서버 보안 하드닝

## 상태

- 상태: 완료
- 브랜치: `feat/server-hardening`

## 목표

인터넷 노출을 전제로 보안 헤더, 요청 한도, 로그 마스킹을 갖춰 동기화 서버를
운영 가능한 수준으로 만든다.

## 의존성

- 120 (서버 비밀 정보 정리 — config 검증 기반 공유)

## 범위

- `@fastify/helmet`으로 보안 헤더를 적용한다.
- `@fastify/rate-limit`으로 전역 완만한 한도와 `/v1/auth/login`·
  `/v1/auth/register` 강화 한도(기본 10회/분/IP 수준)를 적용한다.
- `@fastify/cors`를 도입하되 기본 비활성으로 둔다(허용 오리진은 환경 변수
  목록). 데스크톱 클라이언트는 CORS와 무관하므로 잠금이 안전하다.
- 요청 본문 크기 제한을 설정한다.
- pino `redact`로 authorization 헤더를 로그에서 마스킹한다.
- 신규 환경 변수를 120에서 시작한 config 검증에 편입한다.

## 제외 범위

- HTTPS 종단(리버스 프록시 몫 — 220의 배포 문서에서 지침 제공).
- WAF, 감사 로그.

## 설계 결정

- rate-limit 저장소는 인메모리로 한다(단일 인스턴스 전제 — 220 배포 문서에
  명시). 다중 인스턴스가 필요해지면 별도 작업으로 등록한다.
- 한도 기본값과 환경 변수 이름은 `server/README.md`에 문서화한다.
- 전역 기본 한도는 IP별 300회/분, 회원 가입과 로그인은 각 경로별 10회/분으로
  한다. 본문 기본 한도는 256KiB다.
- CORS origin은 쉼표로 구분한 정확한 HTTP(S) origin만 허용하며 wildcard와 경로가
  포함된 URL을 거부한다.
- Fastify 5 호환 범위와 설정 방식은 Fastify 팀의 공식 문서를 기준으로 했다.
  - https://github.com/fastify/fastify-helmet
  - https://github.com/fastify/fastify-rate-limit
  - https://github.com/fastify/fastify-cors
  - https://fastify.dev/docs/latest/Reference/Server/
  - https://fastify.dev/docs/latest/Reference/Logging/

## 완료 조건

- 한도 초과 요청이 429를 반환한다.
- 응답에 보안 헤더가 존재한다.
- 기존 auth·sync 테스트가 전부 통과한다.

## 검증

- `pnpm test`에 rate-limit(429)과 보안 헤더 존재 테스트를 추가한다.
- 로컬 기동 후 curl로 헤더와 한도를 수동 확인한다.

### 실행 결과

- `pnpm build`: 성공.
- `pnpm test`: 5개 파일, 20개 테스트 통과.
- 로컬 서버를 빌드 산출물로 기동하고 `curl`로 `/health` 응답의 CSP, HSTS,
  `X-Content-Type-Options`, `X-Frame-Options` 헤더를 확인했다.
- 같은 IP에서 유효하지 않은 로그인 요청을 연속 전송해 최초 10회는 400, 11번째는
  429와 `Retry-After` 헤더를 반환하는 것을 확인했다.

## 작업 결과

- 커밋: `36f30b8` (`feat(server): harden public API`)
- PR: https://github.com/JeongHoeMin/Hm-Desktop-Calendar/pull/26
