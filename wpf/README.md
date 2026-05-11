# TimetableScheduler — WPF 포팅

Python 프로토타입(`../`)의 C#/WPF 이식. OR-Tools CP-SAT + MVVM(CommunityToolkit) + SQLite.

## 실행

```powershell
cd wpf
dotnet run --project TimetableScheduler.Wpf
```

처음 실행 시 `../개설강좌 편람.xlsx`를 자동 임포트 (29 과목 / 12 교수 / 9 강의실). DB 파일은 exe 옆 `timetable.db`.

테스트:
```powershell
dotnet test TimetableScheduler.Tests
```

## 솔루션 구조

7개 프로젝트, **net8.0** (Wpf만 `net8.0-windows`).

```
TimetableScheduler.Domain      순수 도메인 (Course/Professor/Room/CrossGroup/RetakeScenario/TimeSlot)
       ↓
TimetableScheduler.Solver      OR-Tools CP-SAT + HC/SC + 4-phase lex 다양성 솔버
       ↓
TimetableScheduler.Scoring     SC raw 점수 + Top-M 랭킹
       ↓
TimetableScheduler.Data        SQLite Repository + ClosedXML xlsx 로더
       ↓
TimetableScheduler.ViewModel   WorkspaceService + 페이지/그리드 VM + DI 확장
       ↓
TimetableScheduler.Wpf         XAML View + Controls + Converters

TimetableScheduler.Tests       xUnit (94개)
```

의존성 화살표는 단방향. Domain은 외부 의존성 0.

## 핵심 클래스

| 위치 | 클래스 | 역할 |
|---|---|---|
| Domain | `Course`, `Professor`, `Room`, `TimeSlot`, `DomainHelpers` | 도메인 |
| Solver | `ModelBuilder`, `BasicHcs`, `BlockHcs`, `GroupingHcs` | HC 모델 빌드 |
| Solver | `SoftConstraints`, `DiverseSolver` | SC + 4단계 lex + 시드 loop |
| Solver | `TimetableRuns`, `ConflictDetector` | 연속 교시 병합 + HC 위반 검출 |
| Scoring | `SolutionScoring` | SC raw 점수 + `Rank` |
| Data | `XlsxLoader`, `SqliteRepository`, `AppData` | xlsx 파싱 + DB 영속 |
| ViewModel | `WorkspaceService` | 단일 진실원, 모든 변경 즉시 DB 저장 |
| ViewModel | `SolverService` | `Task.Run` + `IProgress` + `CancellationToken` |
| ViewModel | `TimetableGridViewModel`, `UnifiedTimetableViewModel` | 격자 VM (공유) |
| ViewModel | `MainWindowViewModel`, 4개 Page VM | 네비게이션 + 페이지 |

## HC/SC 매핑 (Python 동일)

| HC | 의미 | 위치 |
|----|------|------|
| 01 | 강의실 동시 1개 | basic |
| 02 | 교수 동시 1개 (팀티칭, **y**) | basic |
| 03 | 교수 불가능 시간 | basic |
| 04 | 시수 충족 (`Σ x = hours·K`) | basic |
| 06 | 블록 연속 + 다중방 동시 점유 | blocks |
| 08 | 분반 중복 금지 (**y**) | basic |
| 11 | 같은 학년 중복 (분반/Cross 제외, **y**) | basic |
| 12 | 점심(5교시) 금지 | basic |
| 13 | `is_fixed` 과목 시간 슬롯 강제 (강의실 별도) | basic |
| 14 | `fixed_rooms` 외 사용 금지 | blocks |
| 15 | 같은 교수 분반 인접 | blocks |
| 16 | Cross cyclic shift (**y**) | grouping |
| 17 | 재수강 안전 분반 (**y**) | grouping |
| 18 | 블록 페어 요일 차 ≤ 2 | blocks |
| 19 | 블록 시작 교시 ∈ {1,3,6,8} | blocks |
| 20 | 같은 과목 블록들 다른 요일 | blocks |
| 21 | 교수 단위 강의실 일관성 | blocks |

| SC | 의미 | weight |
|----|------|--------|
| 01 | 월오전/금오후 회피 | 1 |
| 02 | 교수당 강의 요일 ≤ 3 | 1 |
| 03 | 블록 페어 요일 간격 ≥ 2 | 1 |

## 4단계 lex 솔버 흐름

`DiverseSolver.Solve`:
1. **Phase 1A** — SC-01 opt 측정 → `sc01Bound` 잠금
2. **Phase 1B** — SC-02 opt (SC-01 제약 유지) 잠금
3. **Phase 1C** — SC-03 opt (SC-01/02 유지) 잠금
4. **Phase 2** — 본 모델 + 모든 SC bound + 시드 loop (다양성 확보, `randomize_search:true`)

각 phase 일회용 모델 (`ModelBuilder.Build`). `IProgress<SolverProgress>` + `CancellationToken` 지원.

## 4개 화면

| # | 이름 | VM | View |
|---|------|----|----|
| 1 | 시간표 선택 | `TimetableSelectionViewModel` | `TimetableSelectionView.xaml` |
| 2 | 정보 입력 | `DataInputViewModel` | `DataInputView.xaml` |
| 3 | 해 미리보기 | `ResultsViewModel` | `ResultsView.xaml` |
| 4 | 수동 편집 | `ManualEditViewModel` | `ManualEditView.xaml` |

네비게이션: `MainWindow` ContentControl + DataTemplate.

### 2번 화면 (정보 입력)

좌측 사이드바 (Resource Explorer) — 교수/교과목/강의실/솔버 nav.

- **교수 깊은 편집**: AllowedRooms 체크리스트 + UnavailableSlots 5×9 토글 그리드
- **교과목 깊은 편집**: IsFixed, FixedRooms 체크리스트, FixedSlots 5×9, BlockStructure CSV, CoteachProfs 체크리스트
- **xlsx 가져오기** 버튼
- **🪄 시간표 자동 생성**: SC 토글 + total_solutions/time_limit_sec + 진행바 + 취소

### 3번 화면 (해 미리보기)

상단 해 선택 드롭다운 + 점수 표시 + 학년 색상 범례 + 모든 학년 펼침 토글.
중첩 TabControl로 4개 뷰:
1. **통합 시간표** — 학년별 컬럼 분할 (Python `render_unified` 동일), 색상, 셀 병합
2. **학년별** — 1/2/3/4학년 서브탭
3. **강의실별** — 강의실당 서브탭
4. **교수별** — 교수당 서브탭

학년 색상: 1=#FFF9C4(노랑), 2=#DCEDC8(연두), 3=#BBDEFB(하늘), 4=#FFCDD2(빨강) — Python 동일.

### 4번 화면 (수동 편집)

통합 시간표 셀 클릭 → 우측 320px Inspector 활성화 → 강의실 드롭다운 변경 → Apply Changes. `ConflictDetector`가 HC-01/02/08/12 위반 카드 표시. 재솔버 호출은 없음 — 메모리상의 수정만.

## 자동 임포트 / DB

- exe 위치부터 위로 8단계까지 `개설강좌 편람.xlsx` 탐색 → 자동 임포트 (DB 비었을 때만)
- 모든 CRUD는 즉시 SQLite 영속 (`WorkspaceService.Persist`)
- List 필드 (FixedRooms, BlockStructure, FixedSlots, UnavailableSlots, AllowedRooms, CoteachProfs, BaseIds)는 JSON 컬럼

## Python ↔ C# 차이

- **Python 동결** — 베이스라인. 변경 없음.
- C# 솔버는 `IProgress<>` + `CancellationToken` 지원 (Python엔 없음)
- DB는 SQLite (Python은 `data_source.py` 코드 생성)
- 결과: 같은 xlsx 입력 → 동일 HC 만족 + 동일 SC 점수 (`Xlsx_ToSolver_*` 통합 테스트로 검증)

## 테스트 94개

| 영역 | 개수 |
|---|---|
| Domain (DomainHelpers, ModelDefaults) | 11 |
| Solver (OR-Tools smoke, HC coverage, DiverseSolver, TimetableRuns, ConflictDetector) | 22 |
| Scoring | 8 |
| Data (XlsxLoader, SqliteRepository, Python baseline 비교) | 9 |
| ViewModel (Workspace, Grid, Unified, MainWindow, ManualEdit, CheckList, TimeSlotPicker) | 30 |
| Integration (end-to-end xlsx → 솔버) | 3 |
| Solver smoke | 3 |
| ModelBuilder smoke | 4 |
| HC coverage extra | 4 |
| **계** | **94** |

## 다음에 할 일 (선택)

- 시간 이동 (Day/Period 변경) + 드래그-드롭 수동 편집
- 수동 편집 후 재솔버 트리거 (변경 위주 미니 솔버)
- Export 기능 (Excel/PDF)
- CrossGroup / RetakeScenario CRUD UI
