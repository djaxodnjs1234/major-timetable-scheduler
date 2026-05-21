# 기존 시간표 제약조건 편집 — 세션 격리

## 목표

1번(시간표 선택)에서 기존 시간표를 고르면 2번(정보 입력)으로 가서 그 시간표의
제약조건(스냅샷)을 수정할 수 있다. 2번에서 "해 계산"(→3번) 또는 "수동 편집으로"(→4번)
두 출구로 나간다. 수정 결과는 항상 새 시간표로 저장된다.

## 흐름

```
1번 시간표 선택
  ├─ [+ 새 시간표 만들기] → 2번 (전역 워크스페이스, 새 시간표 모드)
  └─ [편집]              → 2번 (그 시간표 스냅샷, 기존 편집 모드)

2번 정보 입력 — 출구 2개:
  ├─ [해 계산]      → 3번 해 미리보기   (항상 활성)
  └─ [수동 편집으로] → 4번 수동 편집      (기존 편집 모드일 때만; 새 시간표 모드는 비활성)

4번에서 저장 → 항상 새 시간표로 저장 → 1번
```

## 핵심 설계 — WorkspaceService 세션 모드

`WorkspaceService`를 두 모드로:

| | 전역 모드 (기존) | 세션 모드 (신규) |
|---|---|---|
| 인스턴스 | DI 싱글톤 1개 | 작업마다 임시 1개 |
| 데이터 출처 | 메인 DB (`Reload`) | 시간표의 SnapshotJson |
| CRUD persist | 메인 DB 즉시 저장 | 메모리만, DB 안 건드림 |
| `SavedTimetables` | 메인 DB | (세션은 시간표 목록 불필요) |

- 세션 모드 = DB 저장 비활성. CRUD는 ObservableCollection 메모리 변경만.
- 세션 워크스페이스는 `AppData`(스냅샷)로 초기화.
- 핵심 이점: `DataInputView` XAML/CRUD 코드는 거의 그대로. ViewModel이 바라보는
  `WorkspaceService` 인스턴스만 전역↔세션으로 교체.

## 단계

### 1. WorkspaceService — 세션 모드 지원
- 생성자/팩토리: `WorkspaceService.CreateSession(AppData snapshot)` —
  repo 없이 메모리 컬렉션만 채우는 세션 인스턴스.
- `Persist()`가 세션 모드면 DB 저장 스킵 (`_repo == null` 가드).
- `SaveTimetable`/`Reload`/`ExportDatabase` 등 repo 의존 메서드는 세션 모드에서
  호출 안 되도록 (또는 가드).
- verify: 단위 테스트 — 세션 워크스페이스 CRUD가 메인 DB 안 건드림.

### 2. DataInputViewModel — 활성 워크스페이스 전환
- `Workspace` 프로퍼티를 교체 가능하게 (`[ObservableProperty]` 또는 메서드).
- `LoadForNewTimetable()` — 전역 워크스페이스 사용 (새 시간표 모드).
- `LoadForExistingTimetable(SavedTimetableRecord)` — 스냅샷으로 세션 워크스페이스 생성.
- 모드 플래그 `IsExistingMode` — "수동 편집으로" 버튼 활성 조건.
- 출구 이벤트: `SolveRequested`(기존 `GoToSelectionRequested` 재사용/개명 검토),
  `GoToManualRequested`.
- `SolveAsync`는 활성 워크스페이스를 솔버에 넘김.
- verify: 빌드.

### 3. ManualEditViewModel — 2번에서 받은 세션 데이터로 진입
- 기존 시간표 모드에서 "수동 편집으로" → 그 시간표 배치 + 세션 스냅샷으로 `LoadFromSaved`
  유사 진입. (이미 `_sessionData` 메커니즘 있음 — 재사용.)
- verify: 빌드.

### 4. MainWindowViewModel — 네비게이션 재배선
- `_selection.EditRequested` → 4번 직행이 아니라 **2번**으로
  (`_input.LoadForExistingTimetable` 후 `NavigateTo(_input)`).
- `_selection.CreateNewRequested` → `_input.LoadForNewTimetable` 후 2번.
- `_input.SolveRequested` → 3번, `_input.GoToManualRequested` → 4번.
- verify: 빌드.

### 5. DataInputView.xaml — 출구 버튼 2개
- 솔버 패널: 기존 "다음으로 넘어가기" → "해 계산" (→3번).
- "수동 편집으로" 버튼 추가 — `IsExistingMode`일 때만 보임/활성.
- verify: 앱 실행.

### 6. TimetableSelectionView — '편집' 버튼은 그대로 (2번으로 가게 됨)
- 버튼 라벨/동작 점검. `EditCommand`는 그대로, 네비게이션 타겟만 4단계에서 바뀜.

### 7. 검증
- `dotnet build` + `dotnet test`.
- 앱 실행: 기존 시간표 편집 → 2번에서 제약조건 수정 → 해 계산/수동 편집 양쪽 →
  저장 시 새 시간표로 추가 → 메인 DB의 다른 데이터 안 변함 확인.

## 결정 사항

- 1번 '편집' → 2번(정보 입력), 거기서 제약조건 수정.
- 2번 출구 분리: 해 계산(→3번, 항상) / 수동 편집(→4번, 기존 모드만).
- 새 시간표 모드는 배치가 없어 수동 편집 직행 불가.
- 편집 대상은 그 시간표의 SnapshotJson (전역 워크스페이스 아님).
- 세션 모드 CRUD는 메인 DB 안 건드림. 최종 결과는 4번 저장 시 새 시간표로.
- 저장은 항상 새 시간표 (원본 보존).

## 리스크

- `DataInputView`의 persist 콜백(`set_persist_callback`)·자동 silent 저장이 세션 모드와
  충돌할 수 있음 — 세션 모드에선 persist 무력화 필요.
- WorkspaceService 인스턴스 교체 시 `Changed` 이벤트 구독을 새 인스턴스로 재배선해야 함.
- 큰 변경 — 단계별 빌드 검증 필수.

## Verification Result (done)

- Persist-callback risk turned out moot: WPF has no `set_persist_callback`
  (Python-prototype-only). `WorkspaceService.Persist()` calls `_repo` directly;
  guarded with `_repo?.` so session mode skips DB writes.
- `DataInputViewModel._workspace` is swappable; `SwitchWorkspace` re-binds the
  `Changed` handler and raises `OnPropertyChanged(nameof(Workspace))` so the XAML
  follows the new instance.
- Session snapshot flows end-to-end: solver runs on the session workspace, and
  `ResultsViewModel`/`ManualEditViewModel` render against that snapshot via
  `SessionCourses/Rooms/Professors` helpers.
- Full solution clean build: 0 errors. 157 tests pass.
- New/updated tests: `Edit_NavigatesToInputPageInExistingMode`,
  `EditThenGoToManual_NavigatesToManualPage`, `CreateNew_DisablesGoToManual`,
  `SaveTimetable_NavigatesBackToSelectionPage`, `CreateSession_CrudDoesNotTouchMainDb`.
- App runs without crash — swapped ViewModels and DataInputView XAML load fine.
- Unrelated pre-existing 9 failures: `개설강좌 편람.xlsx` location issue (`FindRepoRoot`).
