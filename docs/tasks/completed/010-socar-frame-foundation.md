# 010 쏘카프레임 디자인 기반 이식

## 상태
- 상태: 완료
- 브랜치: `feat/socar-frame-foundation`

## 목표
React 전용 쏘카프레임 2.0의 시각 토큰과 UX 원칙을 Avalonia 네이티브 디자인
시스템으로 이식해 이후 화면 작업의 일관된 기반을 만든다.

## 의존성
- 작업 000 완료

## 범위
- 공식 색상, 타이포그래피, 간격, 모서리와 상태 토큰을 Avalonia 리소스로 정의한다.
- Pretendard 글꼴과 라이선스를 포함한다.
- 공통 Button, TextBox, ComboBox, Flyout, Dialog와 Card 스타일을 만든다.
- 메인 창과 로그인 창을 공통 스타일로 전환한다.

## 제외 범위
- 편집 창의 전체 UX 개편과 새 기능 입력 필드는 다루지 않는다.
- React/Tailwind 패키지를 데스크톱 앱에 직접 의존시키지 않는다.

## 설계 결정
- 공식 Foundation과 UX 문서를 1차 자료로 사용하고 참조 버전을 기록한다.
- 토큰은 의미 기반 이름을 사용하며 화면에 색상 값을 직접 작성하지 않는다.
- Pretendard를 앱 리소스로 제공해 시스템 설치 여부에 의존하지 않는다.
- 2026년 7월 14일 기준 쏘카프레임 V1.0.0과 쏘카 브랜드 팔레트를 적용했다.
- 웹 전용 Foundation 패키지는 직접 의존하지 않고 Avalonia 리소스로 재구성했다.

## 완료 조건
- 공통 리소스만으로 기본 컨트롤 상태와 메인·로그인 화면이 표현된다.
- 최소 창 크기에서 잘림이 없고 키보드 포커스가 구분된다.

## 검증
- Release 빌드가 경고 0개, 오류 0개로 통과했다.
- 기존 데스크톱 부착·입력·위치 회귀 테스트 11개가 모두 통과했다.
- 실제 Explorer 바탕화면 부착 상태에서 Pretendard와 토큰 리소스 해석, 1080×969 렌더링을 확인했다.
- PR 본문에 변경 전후 스크린샷과 공식 출처를 첨부했다.

## 참고 자료
- [쏘카프레임 Foundation 설정](https://socarframe.socar.kr/development/foundation)
- [쏘카프레임 디자인 원칙](https://socarframe.socar.kr/development/principle)

## 작업 결과
- 커밋: `0838157`, `06f579b`
- PR: https://github.com/JeongHoeMin/Hm-Desktop-Calendar/pull/4
