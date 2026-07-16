# 220 서버 배포 패키징

## 상태

- 상태: 완료
- 브랜치: `feat/server-deployment`

## 목표

Dockerfile, docker-compose와 배포 문서를 제공해 동기화 서버를 재현 가능하게
배포·운영할 수 있게 한다. 현재는 개발 실행 방법만 문서화돼 있다.

## 의존성

- 210 (서버 보안 하드닝 — 하드닝이 반영된 상태로 패키징)

## 범위

- 멀티스테이지 `server/Dockerfile`(node:22-slim + corepack pnpm, 빌드 산출물
  `dist`만 복사).
- `server/docker-compose.yml`: server + postgres:16, `/health`를 컨테이너
  healthcheck로 연결, DB 준비 후 서버 기동 순서 보장.
- 배포 문서(`server/README.md` 확장 또는 `server/docs/deployment.md`):
  환경 변수 준비, 리버스 프록시/TLS 지침, `pg_dump` 백업 지침, 마이그레이션
  운영 절차, 단일 인스턴스 전제(210의 rate-limit) 명시.
- compose 기동 후 register/login/sync를 확인하는 curl 스모크 절차를 문서에
  기록한다.

## 제외 범위

- CI/CD 파이프라인.
- 특정 클라우드 배포 절차.
- 다중 인스턴스 구성.

## 설계 결정

- 마이그레이션은 별도 job 없이 서버 기동 시 기존 advisory lock 로직으로
  실행한다. 이미 동시 기동에 안전하다.
- compose의 비밀 값은 `.env` 파일 참조로 통일하고 compose 파일에 직접 넣지
  않는다.
- 서버 포트는 기본적으로 호스트 loopback에만 바인딩하고 TLS 리버스 프록시만
  외부에 노출한다. PostgreSQL 포트는 호스트에 공개하지 않는다.
- 런타임 이미지는 비루트 `node` 사용자로 실행하며 Node 자체 `fetch`를
  healthcheck에 사용해 curl 패키지를 추가하지 않는다.
- Docker 공식 문서를 기준으로 멀티스테이지 빌드와 `service_healthy` 기동 순서를
  구성했다.
  - https://docs.docker.com/build/building/multi-stage/
  - https://docs.docker.com/compose/how-tos/startup-order/
  - https://github.com/nodejs/docker-node
  - https://hub.docker.com/_/postgres/

## 완료 조건

- `docker compose up`만으로 빈 DB에서 서버가 기동하고 register/login/sync가
  동작한다.
- 이미지 빌드가 성공하고 healthcheck가 정상 전환된다.

## 검증

- 이미지 빌드와 compose 기동을 실제 수행한다.
- 문서의 curl 스모크 절차를 그대로 따라 확인하고 결과를 PR에 기록한다.

### 현재 검증 결과

- `pnpm install --frozen-lockfile`: 성공(pnpm 10.32.1).
- `pnpm build`: 성공.
- `pnpm test`: 6개 파일, 23개 테스트 통과. 배포 구성 테스트가 Compose YAML 파싱,
  PostgreSQL health 의존성, loopback 포트, 비루트 멀티스테이지 이미지와 비밀 값
  비포함을 검사한다.
- WSL Ubuntu의 Docker Engine 29.1.3과 Compose 2.40.3에서 `docker compose build`가
  성공했다.
- 빈 PostgreSQL 16 볼륨에서 `docker compose up -d --wait` 실행 후 DB와 서버가 모두
  `healthy`로 전환됐다. 서버 기동 시 마이그레이션 2건이 적용됐다.
- 문서의 curl 절차로 가입(201), 로그인(200), v2 동기화(200)를 확인했다.
- 런타임 컨테이너는 UID 1000으로 실행되며 `/app/src`가 없고 `/app/dist`만 포함하며
  TypeScript 등 개발 의존성이 제거된 것을 확인했다.
- 검증 후 Compose 컨테이너, 네트워크와 테스트 PostgreSQL 볼륨을 삭제했다.

## 작업 결과

- 커밋: `d45c68d`
- PR: https://github.com/JeongHoeMin/Hm-Desktop-Calendar/pull/27
