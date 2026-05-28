# 수동 편집 → 시간표 선택 네비게이션

## 목표

수동 편집 화면(4번)에서 시간표를 저장하면 시간표 선택 화면(1번)으로 자동 이동한다.

## 현재 구조

- `ManualEditViewModel.SaveTimetable` (`[RelayCommand]`) — `_workspace.SaveTimetable` 호출이 종점.
  저장 후 화면 전환 없음.
- `MainWindowViewModel`이 페이지 VM의 네비게이션 이벤트를 구독해 `NavigateTo`.
  기존 패턴: `_selection.CreateNewRequested`, `_selection.EditRequested`,
  `_results.EditSelectedRequested`, `_input.GoToSelectionRequested`.

## 단계

### 1. ManualEditViewModel — SavedRequested 이벤트
- `event EventHandler? SavedRequested` 추가.
- `SaveTimetable()` 끝(저장 성공 후)에 `SavedRequested?.Invoke(this, EventArgs.Empty)`.
- verify: 빌드.

### 2. MainWindowViewModel — 구독
- 생성자에서 `_manual.SavedRequested += (_, _) => NavigateTo(_selection);`
- verify: 빌드.

### 3. 검증
- `dotnet build` + `dotnet test` 통과.
- 앱 실행: 수동 편집에서 저장 → 시간표 선택 화면으로 이동 + 저장된 시간표가 목록에 보임.

## 결정 사항

- 저장 후 자동 이동 (별도 버튼 없음). 저장이 수동 편집의 종점이고,
  저장된 시간표는 시간표 선택 목록에 바로 나타나므로 자연스러운 흐름.

## 추가 요구사항 (대화 중 추가)

상단 네비 바를 제거하면서 페이지 타이틀이 사라짐 → 사용자가 현재 단계를 인지하기 어려움.

### 4. 4개 화면에 일관된 단계 헤더 추가
- 각 화면 최상단에 단계 헤더 바: "N단계 · 화면 이름" + 짧은 설명.
  - 시간표 선택: "1단계 · 시간표 선택"
  - 정보 입력: "2단계 · 정보 입력" (기존 자체 타이틀 영역과 통합)
  - 해 미리보기: "3단계 · 해 미리보기"
  - 수동 편집: "4단계 · 수동 편집"
- DataInputView의 디자인 토큰(`#005FB8` primary, Surface 색) 기준으로 통일.
- verify: 4개 화면 모두 상단에 단계명 보임.

### 5. 새로 추가한 버튼 스타일 통일
- 시간표 선택의 `+ 새 시간표 만들기` → 공유 `PrimaryButton` 스타일 적용.
- 목록 `편집` 버튼 → 행 크기에 맞춰 컴팩트 유지, primary 색은 기존대로.
- 해 미리보기/정보 입력의 "다음/편집" 흐름 버튼 → 녹색은 "앞으로 진행" 시그널로 의도적 유지,
  패딩은 이미 일관(`14,10`).
- verify: 빌드 + 앱 실행해 시각 확인.

## 검증 결과 (완료)

- 전체 솔루션 클린 빌드: 오류 0 (XAML 포함).
- 네비게이션/VM 테스트 65개 전부 통과. 신규: `SaveTimetable_NavigatesBackToSelectionPage`.
- 앱 정상 실행/종료 — 4개 View 새 헤더·공유 스타일 바인딩 오류 없음.
- 공유 디자인 토큰은 `App.xaml`에 정의: `StepHeaderBar`/`StepBadge`/`StepTitle`/
  `StepCaption`/`PrimaryButton` + `AppPrimary` 등 브러시.
- 각 화면 상단 단계 헤더: 1단계 시간표 선택 / 2단계 정보 입력 / 3단계 해 미리보기 /
  4단계 수동 편집 — 배지 + 제목 + 한 줄 설명.
