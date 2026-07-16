# 동기화 서버 배포

## 전제와 구성

이 배포 패키지는 Docker Compose 단일 서버 인스턴스와 PostgreSQL 16 한 개를
기준으로 한다. 서버의 rate-limit 상태가 프로세스 메모리에 있으므로 서버 서비스를
여러 인스턴스로 확장하면 안 된다. 데이터베이스 포트는 호스트에 공개하지 않고,
서버도 기본적으로 호스트의 `127.0.0.1:3000`에만 바인딩한다.

`Dockerfile`은 `node:22-slim` 빌드 단계에서 고정된 pnpm 10.32.1로 의존성을
설치하고 TypeScript를 빌드한다. 런타임 단계에는 운영 의존성, `package.json`과
`dist`만 복사하며 `node` 사용자로 실행한다.

## 최초 기동

1. `server/.env.docker.example`을 `server/.env.docker`로 복사한다.
2. `POSTGRES_PASSWORD`를 충분히 긴 무작위 값으로 바꾸고 `DATABASE_URL` 안의
   비밀번호도 같은 값으로 바꾼다. URL 예약 문자가 있으면 percent-encoding한다.
3. `JWT_ACCESS_SECRET`과 `TOKEN_HASH_SECRET`을 서로 다른 32바이트 이상의 무작위
   값으로 바꾼다. 실제 `.env.docker`는 커밋하거나 공유하지 않는다.
4. `server` 디렉터리에서 다음 명령을 실행한다.

```sh
docker compose build
docker compose up -d --wait
docker compose ps
curl --fail http://127.0.0.1:3000/health
```

Compose는 PostgreSQL의 `pg_isready` healthcheck가 통과한 뒤 서버를 시작한다.
서버 healthcheck는 `/health`가 데이터베이스 쿼리까지 성공해야 정상으로 전환된다.
서버 시작 시 아직 적용되지 않은 SQL 마이그레이션을 advisory lock 안에서 파일명
순서대로 적용한다.

## API 스모크 확인

아래 절차에는 `curl`과 `jq`가 필요하다. 다른 테스트와 겹치지 않는 이메일을 사용한다.

```sh
BASE_URL=http://127.0.0.1:3000
EMAIL="deploy-smoke-$(date +%s)@example.com"
PASSWORD=replace-with-a-test-password

curl --fail --request POST "$BASE_URL/v1/auth/register" \
  --header "Content-Type: application/json" \
  --data "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"deviceName\":\"deploy-smoke\"}"

LOGIN_RESPONSE=$(curl --fail --request POST "$BASE_URL/v1/auth/login" \
  --header "Content-Type: application/json" \
  --data "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"deviceName\":\"deploy-smoke\"}")
ACCESS_TOKEN=$(printf '%s' "$LOGIN_RESPONSE" | jq --raw-output '.accessToken')

curl --fail "$BASE_URL/v2/sync?after=0&limit=10" \
  --header "Authorization: Bearer $ACCESS_TOKEN"
```

회원 가입은 사용자와 갱신 세션을 만들므로 운영 환경에서는 전용 스모크 계정 정책을
정하거나 별도 검증 데이터베이스에서 실행한다. 이 작업의 완료 검증에서는 격리된
Compose 볼륨을 사용한다.

## 리버스 프록시와 TLS

- 인터넷에는 TLS가 적용된 리버스 프록시의 443 포트만 공개한다. PostgreSQL과
  서버 3000 포트는 외부에 공개하지 않는다.
- 프록시가 `https://calendar.example.com`을 `http://127.0.0.1:3000`으로 전달하게
  하고 유효한 인증서를 자동 갱신한다.
- `/v1/realtime`에는 WebSocket `Upgrade`와 `Connection` 헤더를 전달한다.
- 프록시 요청 본문 한도를 서버의 `BODY_LIMIT_BYTES` 이상으로 맞춘다.
- 브라우저 클라이언트가 있을 때만 공개 HTTPS origin을
  `CORS_ALLOWED_ORIGINS`에 등록한다. 데스크톱 클라이언트에는 CORS가 필요 없다.
- 프록시의 전달 IP 헤더를 신뢰하도록 서버를 바꾸기 전에는 서버 인스턴스를 외부에
  직접 노출하지 않는다. 현재 rate-limit 키는 서버가 직접 관찰한 연결 IP다.

## 배포와 마이그레이션

새 이미지를 배포하기 전에 데이터베이스를 백업한다. 그 뒤 다음 순서로 갱신한다.

```sh
docker compose build --pull
docker compose up -d --wait
docker compose ps
docker compose logs --tail=100 server
```

마이그레이션은 서버 기동 중 트랜잭션과 advisory lock으로 적용된다. 실패하면 서버가
기동하지 않고 해당 마이그레이션 트랜잭션은 롤백된다. 이전 애플리케이션 이미지로
되돌려도 스키마가 자동으로 역마이그레이션되지는 않으므로, 변경 전 백업과 각
마이그레이션의 하위 호환성을 확인한다.

## 백업과 복원

데이터와 계정 복구에는 PostgreSQL 백업이 필요하다. 다음 명령은 custom format
백업을 호스트에 생성한다.

```sh
docker compose exec -T db pg_dump \
  --username calendar --dbname calendar --format=custom \
  > hm-calendar-$(date +%Y%m%d-%H%M%S).dump
```

백업 파일을 별도 장치나 암호화된 저장소에 복제하고 주기적으로 복원 연습을 한다.
복원은 서버를 중지하고 대상 데이터베이스가 비어 있는지 확인한 뒤 수행한다.

```sh
docker compose stop server
cat hm-calendar-YYYYMMDD-HHMMSS.dump | docker compose exec -T db pg_restore \
  --username calendar --dbname calendar --clean --if-exists
docker compose start server
```

`--clean`은 대상 스키마 객체를 삭제하고 다시 만들므로 운영 데이터베이스에서 실행하기
직전에 백업 파일, 대상 Compose 프로젝트와 중지 상태를 다시 확인한다.

## 종료와 상태 확인

```sh
docker compose ps
docker compose logs --tail=100 server
docker compose down
```

`docker compose down`은 컨테이너와 네트워크만 제거하고 named volume은 보존한다.
`docker compose down --volumes`는 데이터베이스를 삭제하므로 운영 환경에서 사용하지
않는다.
