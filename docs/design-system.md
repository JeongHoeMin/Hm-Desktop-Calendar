# Hm Desktop Calendar 디자인 시스템

## 적용 기준

이 앱의 디자인 기반은 쏘카프레임 2.0의 공식 문서와 쏘카 브랜드 팔레트를 Avalonia에 맞게
재구성한 것이다. 확인 기준은 2026년 7월 14일이며, 쏘카프레임 사이트에 표시된 버전은
V1.0.0이다.

웹 전용 `@socar-inc/socar-frame-foundation` 패키지는 Tailwind CSS를 전제로 하므로 앱에 직접
의존하지 않는다. 색상과 타이포그래피의 공개 기준, UX 원칙을 Avalonia 리소스와 스타일로
옮긴다.

## 핵심 원칙

- 기존 Windows 데스크톱 사용 경험을 존중한다.
- 다음 행동을 예측할 수 있도록 같은 역할은 같은 모양과 상태 피드백을 사용한다.
- 누를 수 있는 요소와 입력 가능한 요소의 상태를 명확히 구분한다.
- 화면에서 색상 값을 직접 사용하지 않고 의미 토큰을 사용한다.

## 토큰 규칙

- 주 행동은 쏘카 Blue 06 `#0078FF`를 사용한다.
- 보조 행동과 선택 상태는 Blue 01~03을 사용한다.
- 표면, 테두리와 본문 텍스트는 Gray 01~10 단계로 구분한다.
- 오류와 주의 상태는 공식 상태색 `#FF5065`를 사용한다.
- 간격은 4pt 배수, 모서리는 8·12·16pt 단계로 운용한다.
- 화면은 `SocarPrimaryBrush` 같은 의미 이름만 참조한다.

## 타이포그래피

Pretendard v1.3.9 가변 글꼴을 앱 리소스로 포함한다. 글꼴은 SIL Open Font License 1.1로
배포되며 라이선스 원문은 `Assets/Fonts/Pretendard-LICENSE.txt`에 둔다.

쏘카 공식 타이포그래피 계층을 데스크톱 달력 밀도에 맞춰 다음 역할로 축소 적용한다.

- `display`: 창의 주요 제목
- `title`: 월 제목과 구획 제목
- `body`: 일반 설명과 입력 본문
- `caption`: 상태와 보조 정보

## 공통 구성 요소

`Themes/SocarFrame.axaml`이 Button, TextBox, ComboBox, Flyout, Dialog와 Card의 기본 상태를
정의한다. 주 행동은 `primary`, 보조 행동은 `secondary`, 아이콘 및 낮은 강조 행동은 `ghost`
클래스를 사용한다. 포커스, 마우스 올림, 누름, 비활성 상태는 역할별로 항상 구분되어야 한다.

## 공식 자료

- [쏘카프레임 2.0 Foundation 설정](https://socarframe.socar.kr/development/foundation)
- [쏘카프레임 디자인 원칙](https://socarframe.socar.kr/development/principle)
- [쏘카 브랜드 컬러](https://brand.socar.kr/brandasset)
- [Pretendard 공식 저장소](https://github.com/orioncactus/pretendard)
