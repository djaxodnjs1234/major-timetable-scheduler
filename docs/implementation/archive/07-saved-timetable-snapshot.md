# 저장 시간표 — 제약조건 스냅샷 (B안)

## 목표

시간표 저장 시 그 시점의 워크스페이스 전체(과목/교수/강의실/Cross/Retake)를 함께 직렬화해,
불러온 시간표를 수동 편집할 때 제약조건이 손실되지 않게 한다.

## 현재 한계

- `SavedTimetables` 테이블: `(Id, Name, CreatedAt, AssignmentsJson)` — 배치 정보만.
- 제약조건은 `Courses`/`Professors`/`Rooms`/`CrossGroups`/`RetakeScenarios` 별도 테이블에 있고
  시간표와 연결 안 됨 → 저장 후 정보 입력에서 정의가 바뀌면 사라짐.

## 설계 — 컬럼 1개 추가

```
SavedTimetables (Id, Name, CreatedAt, AssignmentsJson, SnapshotJson)
                                                       ▲ 신규
```

`SnapshotJson` = 저장 시점 `AppData` 전체를 `System.Text.Json`으로 직렬화.
- `WorkspaceService.Snapshot()` 이 이미 `AppData` 반환 → 재사용.
- 불러올 때 스냅샷은 **수동 편집 세션의 검증용으로만** 사용. 전역 워크스페이스 DB는 안 건드림.

## 단계

### 1. 스키마 + 마이그레이션
- `SqliteSchema.CreateAll`: `SavedTimetables`에 `SnapshotJson TEXT` 추가 (신규 DB용).
- `SqliteRepository.EnsureCreated`: 기존 DB 마이그레이션 — `MigrateSavedTimetableSnapshot()`.
  `PRAGMA table_info(SavedTimetables)` 로 컬럼 존재 확인, 없으면
  `ALTER TABLE SavedTimetables ADD COLUMN SnapshotJson TEXT`.
- verify: 기존 DB로 앱 실행 시 예외 없음.

### 2. SavedTimetableRecord에 스냅샷 필드
- `SavedTimetableRecord`에 `string? SnapshotJson` 추가 (nullable — 구 레코드 호환).
- verify: 빌드.

### 3. Repository read/write
- `SavedTimetableRow`에 `SnapshotJson` 프로퍼티 추가.
- `UpsertSavedTimetable`: INSERT에 `SnapshotJson` 포함.
- `LoadSavedTimetables`: `SnapshotJson`을 record로 전달.
- verify: 빌드.

### 4. WorkspaceService.SaveTimetable — 스냅샷 동봉
- `SaveTimetable(name, assignments)` 호출 시 `Snapshot()`을 JSON 직렬화해 record에 포함.
- verify: 단위 테스트 — 저장 후 LoadSavedTimetables로 SnapshotJson이 비어있지 않음.

### 5. ManualEditViewModel.LoadFromSaved — 스냅샷 적용
- `LoadFromSaved(record)`: `record.SnapshotJson`이 있으면 역직렬화해
  수동 편집 세션의 과목/교수/강의실/Cross 소스로 사용.
- 현재 ManualEditViewModel은 `_workspace.ExpandedCourses`/`Professors`/`Rooms`/`CrossGroups`를
  직접 참조 → 스냅샷 적용 시 이 참조를 세션 로컬 데이터로 바꿔야 함.
  - 옵션: `ManualEditViewModel`에 세션 로컬 `IReadOnlyList<Course>` 등 필드를 두고,
    모든 `_workspace.X` 참조를 세션 필드로 교체. 스냅샷 없으면 워크스페이스 폴백.
- verify: 단위 테스트 — 워크스페이스에 없는 과목이 든 스냅샷 시간표를 LoadFromSaved 후
  Grid 렌더 + 검증 정상.

### 6. 검증
- `dotnet build` + `dotnet test` 통과.
- 앱 실행: 저장 → 정보입력에서 과목 수정/삭제 → 시간표 선택에서 그 시간표 편집 →
  원래 제약조건이 보존되는지 확인.

## 결정 사항

- 새 테이블 X — 기존 `SavedTimetables`에 `SnapshotJson` 컬럼 1개 추가.
- 스냅샷은 수동 편집 세션 검증용으로만. 전역 워크스페이스 DB 덮어쓰기 안 함.
- 구 레코드(SnapshotJson NULL) 호환: 스냅샷 없으면 기존대로 워크스페이스 참조.

## 검증 결과 (완료)

- 전체 솔루션 클린 빌드: 오류 0.
- 스냅샷 관련 테스트 68개 전부 통과. 신규 4개:
  - `SaveTimetable_EmbedsWorkspaceSnapshot`
  - `SaveTimetable_SnapshotIsIndependentOfLaterEdits`
  - `LoadFromSaved_UsesSnapshotCoursesNotInWorkspace`
  - `EnsureCreated_OnOldSchema_AddsSnapshotColumnPreservingData`
- 앱이 기존 DB로 정상 실행/종료 — 마이그레이션 예외 없음.
- 무관한 기존 실패 9개: `개설강좌 편람.xlsx` 위치 문제 (`FindRepoRoot`), 이번 작업과 무관.

## 구현 요약

- `ManualEditViewModel`: `_workspace.{ExpandedCourses,Professors,Rooms,CrossGroups}` 참조를
  세션 로컬 프로퍼티 `Session*`로 교체. `_sessionData != null`이면 스냅샷, 아니면 워크스페이스.
- `LoadFromSaved`는 스냅샷 역직렬화 후 `_sessionData` 설정, `LoadFromSolution`은 `_sessionData = null`.
  공통 로직은 `LoadCore`로 추출.
