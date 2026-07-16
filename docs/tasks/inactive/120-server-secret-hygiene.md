# 120 서버 비밀 정보 정리와 검증 규칙

## 상태

- 상태: 비활성
- 브랜치: 미정

## 목표

커밋된 `server/.env.example`에서 실제 DB 접속 정보를 제거하고, placeholder
비밀로는 운영 기동이 불가능하도록 서버 구성 검증을 강화한다. 비밀 관리
규칙을 문서화해 같은 노출이 재발하지 않게 한다.

## 의존성

- 없음. 로드맵에서 가장 먼저 진행한다.
- 실제 DB 자격 증명 회전과 외부 접근 제한은 저장소 밖 운영 조치로, 이 작업과
  별개로 사용자가 수행한다. 수행 여부를 작업 결과에 기록한다.

## 범위

- `server/.env.example`의 `DATABASE_URL`을
  `postgresql://calendar:change-me@localhost:5432/calendar` 형태의
  placeholder로 교체한다.
- `server/src/config.ts`에 `NODE_ENV=production`일 때 placeholder 비밀
  (`replace-with` 접두 시크릿, `change-me`·기본 `postgres:postgres` 계열
  접속 문자열)을 거부하고 기동을 실패시키는 검증을 추가한다.
- `server/README.md`에 비밀 관리 규칙(.env 커밋 금지, 노출 시 회전 절차)
  문단을 추가한다.
- 노출 사실과 자격 증명 회전 완료 여부를 이 문서의 작업 결과에 기록한다.

## 제외 범위

- git 이력 재작성. 자격 증명 회전이 근본 대책이며 공유 이력을 보존한다.
- 실제 DB 비밀번호 회전 자체(운영 조치).
- CORS, rate-limit 등 서버 하드닝(작업 210).

## 설계 결정

- 검증은 config 로드 시점의 fail-fast로 구현한다. Fastify 기동 전에 명확한
  한국어 오류 메시지를 출력하고 프로세스를 종료한다.
- `NODE_ENV !== 'production'`에서는 기존 동작을 유지해 로컬 개발 마찰을
  만들지 않는다.

## 완료 조건

- `git grep 19.19.20.89` 결과가 작업 문서(이 파일) 외 0건이다.
- production 모드에서 placeholder 비밀 조합으로 서버가 기동을 거부한다.
- dev 모드 기동과 기존 테스트가 그대로 동작한다.

## 검증

- config 검증 케이스를 서버 단위 테스트로 추가하고 `pnpm test`를 실행한다.
- `git grep`으로 노출 문자열 잔존 여부를 확인한다.

## 작업 결과

- 커밋: 미정
- PR: 미정
