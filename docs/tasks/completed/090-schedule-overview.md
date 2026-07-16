# 090 일정 모아보기

## 상태
- 상태: 완료
- 브랜치: `feat/schedule-overview`

## 목표
일정을 별도 창에서 검색·필터·정렬하고 실시간 동기화 상태에서
추가·수정·삭제할 수 있는 통합 목록을 제공한다.

## 의존성
- 작업 040, 050, 060, 070 완료
- 작업 순서상 080 병합 후 시작

## 범위
- 메뉴에 일정 모아보기를 추가하고 별도 창을 연다.
- 기본 오늘부터 90일, 현재 월, 30일, 90일과 사용자 기간을 제공한다.
- 제목·메모 검색, 예정·완료·기념일 필터와 날짜·시간 정렬을 제공한다.
- 공용 편집 폼으로 추가·수정·삭제하고 저장소 변경을 디바운스해 반영한다.

## 제외 범위
- 무한 반복 전체 펼치기, 서버 페이지 검색과 내보내기는 지원하지 않는다.

## 설계 결정
- 반복 일정은 선택한 유한 기간 안의 발생 항목만 표시한다.
- 발생 항목 편집은 전체 시리즈 원본을 수정한다.
- 실시간 변경이 연속되면 UI 새로고침을 디바운스한다.

## 완료 조건
- 검색·기간·상태 조합에 맞는 결과가 안정적으로 표시된다.
- 다른 클라이언트 변경과 이 창의 CRUD가 목록과 월간 달력에 함께 반영된다.

## 검증
- 검색·필터·정렬 조합과 저장소 변경 디바운스 회귀 테스트를 추가했다. 공용 편집
  ViewModel의 기존 CRUD·전체 시리즈 테스트와 함께 검증한다.
- `dotnet build tests/HmDesktopCalendar.RegressionTests/HmDesktopCalendar.RegressionTests.csproj --no-restore`
  성공, 경고 0개·오류 0개
- `dotnet run --project tests/HmDesktopCalendar.RegressionTests/HmDesktopCalendar.RegressionTests.csproj --no-build`
  회귀 테스트 48개 통과
- `server`에서 `pnpm test` 재실행, 서버 테스트 9개 통과
- 실제 Windows 창에서 기본 목록, 빈 검색 결과, 다섯 항목 목록과 680×480 최소 창
  크기의 스크롤 동작을 확인한다.
- 스크린샷: [일정 모아보기](../../screenshots/090/090-schedule-overview.png)

## 작업 결과
- 커밋: `51c62e5`
- PR: https://github.com/JeongHoeMin/Hm-Desktop-Calendar/pull/12
