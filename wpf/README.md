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
dotnet test TimetableScheduler.Wpf.Tests
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

TimetableScheduler.Tests       xUnit 회귀 테스트
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
| 18 | SC-03으로 이동: 블록 페어 요일 차 선호 | soft |
| 19 | 블록 시작 교시 ∈ {1,3,6,8} | blocks |
| 20 | 같은 과목 블록들 다른 요일 | blocks |
| 21 | 자동 배정 과목의 교수 강의실 조건 | blocks |
| 22 | 동일 과목 분반의 공통 강의실 | blocks |

강의실 정책: 과목 `FixedRooms`가 있으면 과목 설정이 교수 강의실 조건보다 우선하며, 여러 방이면 모두 동시 점유한다. `FixedRooms`가 없는 자동 과목만 담당 교수의 허용/불가 강의실 조건을 따른다. 같은 과목의 자동 분반은 공통 강의실을 사용하지만, 같은 교수의 서로 다른 과목을 한 방으로 강제하지는 않는다.

Cross 대응 분반이 공통 고정 강의실을 사용하면 같은 시간에 같은 방을 점유하게 되므로 Cross 추가를 차단한다(`IE-039`). 기존 저장 데이터에서 같은 조건은 생성 전 진단으로 표시한다(`GE-028`).

| SC | 의미 | weight |
|----|------|--------|
| 01 | 월오전/금오후 회피 | 1 |
| 02 | 교수당 강의 요일 ≤ 3 | 1 |
| 03 | 블록 페어 요일 차 2 선호 (1 높음, 3~4 낮음) | 1 |

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

- **교수 깊은 편집**: 불가 강의실 체크리스트 + 불가 시간 5×9 토글 그리드
- **교과목 깊은 편집**: IsFixed, 불가 강의실 체크리스트, FixedSlots 5×9, BlockStructure CSV, CoteachProfs 체크리스트
- **강의실 깊은 편집**: 실습실 여부 + 허용 인원 수
- **xlsx 가져오기** 버튼
- **🪄 시간표 자동 생성**: 선호 조건 토글 + 해 후보 수/제한 시간 + Cross 수동 설정 + 진행바 + 취소

### 3번 화면 (해 미리보기)

상단 해 선택 드롭다운 + 점수 표시 + 학년 색상 범례 + 모든 학년 펼침 토글.
중첩 TabControl로 4개 뷰:
1. **통합 시간표** — 학년별 컬럼 분할 (Python `render_unified` 동일), 색상, 셀 병합
2. **학년별** — 1/2/3/4학년 + 대학원 서브탭
3. **강의실별** — 강의실당 서브탭
4. **교수별** — 교수당 서브탭

학년 색상(UI): 1=#FFF9C4(노랑), 2=#DCEDC8(연두), 3=#BBDEFB(하늘), 4=#FFCDD2(빨강), 대학원=#D1C4E9(보라).

### 4번 화면 (수동 편집)

통합 시간표 셀 클릭 → 우측 320px Inspector 활성화 → 강의실 드롭다운 변경 → Apply Changes. `ConflictDetector`가 HC-01/02/08/12 위반 카드 표시. 재솔버 호출은 없음 — 메모리상의 수정만.

## 자동 임포트 / DB

- exe 위치부터 위로 8단계까지 `개설강좌 편람.xlsx` 탐색 → 자동 임포트 (DB 비었을 때만)
- 모든 CRUD는 즉시 SQLite 영속 (`WorkspaceService.Persist`)
- List 필드 (FixedRooms, BlockStructure, FixedSlots, UnavailableSlots, Professor.UnavailableRooms, CoteachProfs, BaseIds)는 JSON 컬럼

## Python ↔ C# 차이

- **Python 동결** — 베이스라인. 변경 없음.
- C# 솔버는 `IProgress<>` + `CancellationToken` 지원 (Python엔 없음)
- DB는 SQLite (Python은 `data_source.py` 코드 생성)
- 결과: 같은 xlsx 입력 → 동일 HC 만족 + 동일 SC 점수 (`Xlsx_ToSolver_*` 통합 테스트로 검증)

## 테스트

도메인·솔버·데이터·ViewModel·WPF 화면 회귀 테스트를 `TimetableScheduler.Tests`와 `TimetableScheduler.Wpf.Tests`에서 관리한다. 최신 실행 방법은 문서 상단의 두 `dotnet test` 명령을 사용한다.

## 다음에 할 일 (선택)

- 시간 이동 (Day/Period 변경) + 드래그-드롭 수동 편집
- 수동 편집 후 재솔버 트리거 (변경 위주 미니 솔버)
- Export 기능 (Excel/PDF)
- CrossGroup / RetakeScenario CRUD UI
