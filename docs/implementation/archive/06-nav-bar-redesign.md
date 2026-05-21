# 페이지 네비게이션 재구성

## 목표

상단 글로벌 네비게이션 바(시간표 선택 / 정보 입력 / 해 미리보기 / 수동 편집)를 제거하고,
각 페이지 안에 흐름에 맞는 "다음으로" 버튼을 두어 4개 화면을 순차적으로 이동하게 한다.

## 현재 구조 (파악 결과)

| # | 페이지 | VM | 역할 |
|---|--------|-----|------|
| 1 | 시간표 선택 | TimetableSelectionViewModel | 시작 화면. 저장된 시간표 목록 + 미리보기 |
| 2 | 정보 입력 | DataInputViewModel | 교과목/교수/강의실 CRUD + 솔버 실행 |
| 3 | 해 미리보기 | ResultsViewModel | 솔버 해 카드 + 시간표 탭 |
| 4 | 수동 편집 | ManualEditViewModel | 해 선택 후 셀 이동/편집 + 저장 |

- 네비게이션은 `MainWindowViewModel`의 `GoTo*Command` 4개 + `MainWindow.xaml` 상단 바.
- `DataInputViewModel`은 이미 `GoToSelectionCommand`(이름과 달리 실제로는 해 미리보기로 이동) +
  `GoToSelectionRequested` 이벤트가 있음 → `MainWindowViewModel`이 구독해 `_results`로 이동.
- `ManualEditViewModel.LoadFromSolution(RankedSolution)` 만 존재. 저장된 시간표 로드 메서드 없음.

## 흐름

```
시간표 선택 ──[+ 새 시간표 만들기]──▶ 정보 입력 ──[솔버 완료 후 다음]──▶ 해 미리보기
   │                                                                        │
   │ [목록 항목의 '편집' 버튼]                            [선택 해 수동 편집]│
   └──────────────▶ 수동 편집 ◀──────────────────────────────────────────────┘
                       │
                       └──[저장 시 SavedTimetables 갱신 → 시간표 선택에서 보임]
```

## 단계

### 1. 상단 네비 바 제거
- `MainWindow.xaml`: 상단 `Border`(Row 0) 안의 버튼 4개 + 페이지 타이틀 `TextBlock` 제거.
  `ContentControl` 하나만 남긴다. RowDefinitions 단일화.
- verify: 빌드 성공, 앱 실행 시 상단 바 없음.

### 2. ManualEditViewModel — 저장된 시간표 로드 지원
- `LoadFromSaved(SavedTimetableRecord record)` 추가:
  `record.Assignments`(TimetableAssignmentRow) → `SolutionAssignment` 변환 →
  `RankedSolution`(Score는 0 placeholder) 만들어 `LoadFromSolution` 재사용.
- verify: 단위 테스트 — LoadFromSaved 후 Grid가 렌더되고 BaseSolution이 set 됨.

### 3. MainWindowViewModel — 페이지 간 이동 이벤트 배선
- 각 페이지 VM에 다음 네비게이션 이벤트 추가:
  - `TimetableSelectionViewModel`: `CreateNewRequested`(→정보입력), `EditRequested`(SavedTimetableRecord, →수동편집)
  - `DataInputViewModel`: 기존 `GoToSelectionRequested` 유지 (→해 미리보기). 이름 혼동 있으나 surgical 위해 유지.
  - `ResultsViewModel`: `EditSelectedRequested`(→수동편집)
- `MainWindowViewModel` 생성자에서 구독:
  - `CreateNewRequested` → `NavigateTo(_input)`
  - `EditRequested` → `_manual.LoadFromSaved(record)` 후 `NavigateTo(_manual)`
  - `EditSelectedRequested` → 기존 `GoToManual` 로직 재사용
- `GoTo*Command` 4개는 제거 (상단 바가 유일한 호출처). 단 테스트가 참조 → 4단계에서 처리.
- verify: 빌드 성공.

### 4. 테스트 갱신
- `MainWindowViewModelTests`: `GoToInputCommand`/`GoToResultsCommand` 참조 테스트
  (`GoToInputCommand_NavigatesToInputPage`, `Title_ChangesWithPage`)를 새 이벤트 기반으로 수정.
- verify: `dotnet test` 통과.

### 5. 각 페이지에 네비게이션 버튼 추가 (XAML)
- **시간표 선택**: 좌측 헤더에 `+ 새 시간표 만들기` 버튼 → `CreateNewRequested`.
  목록 `DataTemplate`의 각 행에 `편집` 버튼 → `EditRequested(record)`.
- **정보 입력**: 기존 `다음으로 넘어가기 →` 버튼 유지 (솔버 완료 시 노출). 변경 없음.
- **해 미리보기**: 상단 카드 스트립 영역에 `선택한 해 수동 편집 →` 버튼 → `EditSelectedRequested`.
- **수동 편집**: 저장이 종점. 별도 "다음" 버튼 없음 (저장하면 시간표 선택에서 보임).
- verify: 앱 실행 — 시간표 선택→정보입력→(솔버)→해 미리보기→수동 편집 전 구간 클릭 이동 확인.

### 6. 시간표 선택 화면 — 미선택 시 버튼 비활성화 + 학년/강의실/교수 탭
- '내보내기' 버튼: `SelectedTimetable != null` 일 때만 활성 (이미 `ExportXlsxCommand`에 CanExport 있음 → 버튼 Command 바인딩으로 교체).
- '편집' 버튼(목록 항목)은 항상 활성 (행 자체가 record를 가지므로 미선택 무관). 단 별도 '수동 편집' 진입 버튼을 둔다면 `HasSelection` 바인딩.
- 우측 미리보기 영역: 현재 단일 `UnifiedTimetableControl` → 해 미리보기와 동일한 4탭 TabControl
  (통합/학년별/강의실별/교수별)로 교체.
- `TimetableSelectionViewModel`에 `GradeViews`/`RoomViews`/`ProfessorViews`(ObservableCollection<NamedGridViewModel>) 추가,
  `OnSelectedTimetableChanged`에서 `Preview`와 함께 렌더. ResultsViewModel.RenderCurrent 로직 참고.
- verify: 시간표 선택/해제 시 탭·버튼 상태 정상.

### 7. 수동 편집 화면 — 베이스 해 점수 제거 + 학년/강의실/교수 탭
- `ManualEditView.xaml` 상단 바: `베이스 해:` 라벨 + `BaseSolution.Score.Total` TextBlock 제거.
- 중앙 시간표: 현재 단일 `UnifiedTimetableControl`(편집 가능) → 4탭 TabControl.
  - 통합 탭: 기존 편집 가능 `UnifiedTimetableControl`(Grid VM) 유지.
  - 학년별/강의실별/교수별 탭: 읽기 전용 `TimetableGridControl` (ResultsView와 동일).
- `ManualEditViewModel`에 `GradeViews`/`RoomViews`/`ProfessorViews` 추가, `Rerender()`에서 함께 갱신.
- verify: 통합 탭 셀 이동 정상, 다른 탭은 현재 working 상태 반영.

### 8. 최종 검증
- `dotnet build` + `dotnet test` 통과.
- 앱 실행해 전체 흐름 수동 확인.

## 추가 요구사항 (대화 중 추가)

- 시간표 선택 화면: 미선택 시 내보내기/수동편집 버튼 비활성화. ✅
- 시간표 선택 + 수동 편집 화면: 해 미리보기처럼 학년별/강의실별/교수별 탭 추가. ✅
- 수동 편집 화면: 베이스 해(점수) 표시 제거. ✅

## 검증 결과 (1~8 단계)

- ViewModel 프로젝트 클린 빌드: 오류 0.
- WPF 프로젝트 클린 빌드 (XAML 포함): 오류 0.
- 네비게이션/VM 테스트 61개 전부 통과 (`MainWindowViewModel`/`ManualEdit`/`WorkspaceService`).
- 앱 정상 실행/종료 — XAML 바인딩 오류 없음.
- 무관한 기존 실패 9개: `개설강좌 편람.xlsx`가 레포 루트 → `wpf/data/`로 이동되어
  `XlsxLoaderTests`/`EndToEndSolverTests`/`CompareWithPythonTests`의 `FindRepoRoot()` 실패.
  이번 작업과 무관 (별도 처리 필요).

## 후속 작업 — 저장 시 DB 스냅샷 (B안, 별도 진행)

문제: xlsx 저장/불러오기는 배치 정보(`TimetableAssignmentRow`)만 담아 제약조건
(시수·블록·교수·고정·Cross 등)이 소실됨 → 불러온 시간표 수동 편집 시 검증이 깨짐.

방향: 시간표 저장 시 워크스페이스 스냅샷(과목/교수/강의실/Cross 정의)을 DB에 함께 직렬화.
불러오면 제약조건까지 완전 복원. xlsx는 사람용 출력물로만 유지.
→ 스키마/직렬화 변경이 필요해 별도 계획 파일로 진행.

## 결정 사항

- 시간표 선택→정보 입력 버튼: **"+ 새 시간표 만들기"** (목적 명시형).
- 시간표 선택→수동 편집: **목록 항목마다 '편집' 버튼**.
- 상단 네비 바는 완전 제거 (자유 이동 폐기, 흐름 강제).
