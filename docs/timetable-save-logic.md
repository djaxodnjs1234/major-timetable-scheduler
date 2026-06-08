# 시간표 저장 로직과 DB 테이블 구조

이 문서는 WPF 앱에서 수동 편집 화면의 `저장` 버튼을 눌렀을 때 내부적으로 어떤 코드 경로를 타고, SQLite DB의 어떤 테이블과 칼럼에 저장되는지 정리한다.

## 1. 저장 버튼에서 DB까지의 흐름

### 1) 화면 버튼

- 위치: `wpf/TimetableScheduler.Wpf/Views/ManualEditView.xaml`
- 저장 버튼은 `SaveTimetableCommand`에 바인딩되어 있다.
- 저장 이름 입력칸은 `SaveName`에 바인딩된다.

```xml
<TextBox Text="{Binding SaveName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
<Button Content="💾 저장" Command="{Binding SaveTimetableCommand}" />
```

### 2) 수동 편집 ViewModel 검증

- 위치: `wpf/TimetableScheduler.ViewModel/Pages/ManualEditViewModel.cs`
- 실행 메서드: `SaveTimetable()`
- 저장 전 `ValidateBeforeSave()`를 호출한다.
- `ValidateBeforeSave()`는 `RefreshConflicts(strictManualCrossValidation: true)`로 현재 편집 결과의 제약 위반을 다시 계산한다.
- `ConflictSeverity.Error`가 1건 이상 남아 있으면 저장하지 않고 `StatusMessage`에 저장 차단 메시지를 넣는다.

저장 가능 조건은 다음과 같다.

- `SaveName`이 빈 문자열이 아니어야 한다.
- `BaseSolution`이 있어야 한다.
- 저장 직전 충돌 검사에서 Error가 없어야 한다.

### 3) WorkspaceService 저장 요청

- 위치: `wpf/TimetableScheduler.ViewModel/WorkspaceService.cs`
- 실행 메서드: `SaveTimetable(...)`

`ManualEditViewModel.SaveTimetable()`은 검증 통과 후 다음 데이터를 넘긴다.

| 인자 | 의미 |
|---|---|
| `SaveName.Trim()` | 사용자가 입력한 저장 이름 |
| `_working` | 현재 수동 편집된 실제 시간표 배정 목록 |
| `ToSavedManualCrossLinks()` | 수동 편집에서 유지한 Cross 연결 정보 |
| `_sessionData` | 기존 저장 시간표 수정 또는 세션 편집 시 기준이 되는 설정 스냅샷 |

`WorkspaceService.SaveTimetable()`은 `_working`의 `SolutionAssignment`를 DB 저장용 `TimetableAssignmentRow`로 바꾼다.

```csharp
new TimetableAssignmentRow(a.CourseId, a.Day, a.Period, a.RoomId)
```

그리고 저장 레코드에 새 `Guid` ID와 현재 시각을 붙인 뒤 `SqliteRepository.UpsertSavedTimetable(record)`를 호출한다.

### 4) SQLite 저장

- 위치: `wpf/TimetableScheduler.Data/SqliteRepository.cs`
- 실행 메서드: `UpsertSavedTimetable(SavedTimetableRecord t)`

저장은 하나의 트랜잭션으로 처리된다.

1. `SavedTimetables`에 저장 시간표 본문을 `INSERT OR REPLACE`한다.
2. 같은 저장 시간표 ID에 연결된 기존 `SavedTimetableManualCrossLinks`를 삭제한다.
3. 현재 수동 Cross 연결 목록을 `SavedTimetableManualCrossLinks`에 다시 삽입한다.
4. 트랜잭션을 커밋한다.

저장 성공 후 `WorkspaceService`는 메모리의 `SavedTimetables` 컬렉션 맨 앞에 새 record를 추가한다.

## 2. 저장 시간표 관련 DB 테이블

시간표 저장 기능이 직접 쓰는 핵심 테이블은 두 개다.

1. `SavedTimetables`
2. `SavedTimetableManualCrossLinks`

### SavedTimetables

저장된 시간표 1개당 1행이 생긴다.

| 칼럼 | 타입 | 제약 | 저장 내용 |
|---|---:|---|---|
| `Id` | `TEXT` | `PRIMARY KEY` | 저장 시간표 고유 ID. 저장할 때 새 `Guid` 문자열로 생성된다. |
| `Name` | `TEXT` | `NOT NULL` | 사용자가 입력한 시간표 이름. |
| `CreatedAt` | `TEXT` | `NOT NULL` | 저장 시각. `DateTime.Now`를 ISO-8601 문자열(`ToString("O")`)로 저장한다. |
| `AssignmentsJson` | `TEXT` | `NOT NULL` | 시간표 배정 목록 JSON. 각 항목은 `CourseId`, `Day`, `Period`, `RoomId`를 가진다. |
| `SnapshotJson` | `TEXT` | nullable | 저장 당시의 교과목/교수/강의실/Cross/재수강 설정 전체 스냅샷 JSON. |

`AssignmentsJson`의 항목 구조는 `TimetableAssignmentRow` 기준이다.

| JSON 필드 | 의미 |
|---|---|
| `CourseId` | 배정된 과목/분반 ID |
| `Day` | 요일. 월=0, 화=1, 수=2, 목=3, 금=4 |
| `Period` | 교시 |
| `RoomId` | 배정 강의실 ID |

### SavedTimetableManualCrossLinks

수동 편집에서 유지한 Cross pair 정보를 저장한다. 저장 시간표 하나에 여러 행이 연결될 수 있다.

| 칼럼 | 타입 | 제약 | 저장 내용 |
|---|---:|---|---|
| `Id` | `TEXT` | `PRIMARY KEY` | Cross link 행 고유 ID. 삽입할 때 새 `Guid` 문자열로 생성된다. |
| `SavedTimetableId` | `TEXT` | `NOT NULL` | 연결된 `SavedTimetables.Id`. |
| `SourceCourseId` | `TEXT` | `NOT NULL` | Cross pair의 source 과목 ID. |
| `SourceGrade` | `INTEGER` | `NOT NULL` | source 과목 학년. |
| `SourceSection` | `TEXT` | nullable | source 분반 값. |
| `SourceDay` | `INTEGER` | `NOT NULL` | source 과목 배정 요일. |
| `SourcePeriod` | `INTEGER` | `NOT NULL` | source 과목 배정 교시. |
| `SourceRoomId` | `TEXT` | `NOT NULL` | source 과목 배정 강의실 ID. |
| `TargetCourseId` | `TEXT` | `NOT NULL` | Cross pair의 target 과목 ID. |
| `TargetGrade` | `INTEGER` | `NOT NULL` | target 과목 학년. |
| `TargetSection` | `TEXT` | nullable | target 분반 값. |
| `TargetDay` | `INTEGER` | `NOT NULL` | target 과목 배정 요일. |
| `TargetPeriod` | `INTEGER` | `NOT NULL` | target 과목 배정 교시. |
| `TargetRoomId` | `TEXT` | `NOT NULL` | target 과목 배정 강의실 ID. |
| `PolicyType` | `TEXT` | `NOT NULL` | 수동 Cross 정책 타입 문자열. 현재 저장 로직은 `ManualCrossPolicyType` 값을 넣는다. |
| `CreatedAt` | `TEXT` | `NOT NULL` | 저장 시각. 부모 `SavedTimetables.CreatedAt`과 같은 값이다. |

## 3. SnapshotJson에 들어가는 설정 테이블 구조

`SavedTimetables.SnapshotJson`은 별도 테이블에 나누어 저장하지 않고, 저장 당시 설정 전체를 JSON으로 묶어 저장한다. 이 JSON은 `WorkspaceService.Snapshot()`의 `AppData` 구조를 직렬화한 것이다.

스냅샷에는 다음 컬렉션이 들어간다.

- `Courses`
- `Professors`
- `Rooms`
- `CrossGroups`
- `RetakeScenarios`

즉, 저장된 시간표를 나중에 볼 때 현재 DB의 교과목/교수/강의실 정보가 바뀌었더라도, 저장 당시 이름과 설정 기준으로 렌더링할 수 있다.

아래는 원본 SQLite 스키마 기준의 설정 테이블 칼럼이다. `SnapshotJson` 내부 JSON도 같은 도메인 정보를 담는다.

### Courses

| 칼럼 | 타입 | 제약 | 의미 |
|---|---:|---|---|
| `Id` | `TEXT` | `NOT NULL`, PK 일부 | 과목 ID 또는 base-section ID. |
| `Name` | `TEXT` | `NOT NULL` | 과목명. |
| `Grade` | `INTEGER` | `NOT NULL` | 학년. |
| `HoursPerWeek` | `INTEGER` | `NOT NULL` | 주당 시수. |
| `CourseType` | `TEXT` | `NOT NULL` | 전필/전선/교양 등 과목 유형. |
| `ProfessorId` | `TEXT` | `NOT NULL` | 담당 교수 ID. |
| `Section` | `INTEGER` | `NOT NULL DEFAULT 1`, PK 일부 | 분반 번호. |
| `Department` | `TEXT` | `NOT NULL` | 학과. |
| `FixedRoomsJson` | `TEXT` | `NOT NULL` | 지정 강의실 목록 JSON. |
| `UnavailableRoomsJson` | `TEXT` | `NOT NULL DEFAULT '[]'` | 과목 불가 강의실 목록 JSON. |
| `BlockStructureJson` | `TEXT` | `NOT NULL` | 블록 구조 JSON. 예: `[2,1]`. |
| `IsFixed` | `INTEGER` | `NOT NULL` | 시간 고정 여부. SQLite에서는 0/1로 저장. |
| `FixedSlotsJson` | `TEXT` | `NOT NULL` | 고정 시간 슬롯 목록 JSON. |
| `CoteachProfsJson` | `TEXT` | `NOT NULL` | 팀티칭 교수 ID 목록 JSON. |

### Professors

| 칼럼 | 타입 | 제약 | 의미 |
|---|---:|---|---|
| `Id` | `TEXT` | `PRIMARY KEY` | 교수 ID. |
| `Name` | `TEXT` | `NOT NULL` | 교수명. |
| `UnavailableSlotsJson` | `TEXT` | `NOT NULL` | 교수 불가능 시간 목록 JSON. |
| `AllowedRoomsJson` | `TEXT` | `NOT NULL` | 교수 허용 강의실 목록 JSON. |
| `UnavailableRoomsJson` | `TEXT` | `NOT NULL DEFAULT '[]'` | 교수 불가 강의실 목록 JSON. |

### Rooms

| 칼럼 | 타입 | 제약 | 의미 |
|---|---:|---|---|
| `Id` | `TEXT` | `PRIMARY KEY` | 강의실 ID. |
| `Name` | `TEXT` | `NOT NULL` | 강의실 이름. |
| `IsLab` | `INTEGER` | `NOT NULL DEFAULT 0` | 실습실 여부. SQLite에서는 0/1로 저장. |
| `Capacity` | `INTEGER` | `NOT NULL DEFAULT 0` | 수용 인원. |

### CrossGroups

| 칼럼 | 타입 | 제약 | 의미 |
|---|---:|---|---|
| `Id` | `TEXT` | `PRIMARY KEY` | Cross 그룹 ID. |
| `BaseIdsJson` | `TEXT` | `NOT NULL` | Cross로 묶인 과목 base ID 목록 JSON. |

### RetakeScenarios

| 칼럼 | 타입 | 제약 | 의미 |
|---|---:|---|---|
| `CurrentGrade` | `INTEGER` | `NOT NULL`, PK 일부 | 현재 학년. |
| `RetakeBaseId` | `TEXT` | `NOT NULL`, PK 일부 | 재수강 대상 과목 base ID. |

## 4. 불러오기와 삭제 로직

### 저장 시간표 불러오기

`SqliteRepository.LoadSavedTimetables()`는 다음 순서로 동작한다.

1. `SavedTimetableManualCrossLinks` 전체를 읽고 `SavedTimetableId` 기준으로 그룹화한다.
2. `SavedTimetables`를 `CreatedAt DESC` 순서로 읽는다.
3. `AssignmentsJson`을 `List<TimetableAssignmentRow>`로 역직렬화한다.
4. 같은 `SavedTimetableId`의 Cross link 목록을 붙여 `SavedTimetableRecord`를 만든다.

### 저장 시간표 삭제

`DeleteSavedTimetable(id)`는 다음 순서로 삭제한다.

1. `SavedTimetableManualCrossLinks`에서 `SavedTimetableId = id`인 행 삭제.
2. `SavedTimetables`에서 `Id = id`인 행 삭제.

## 5. 핵심 요약

저장 버튼을 누르면 실제로는 다음 데이터가 저장된다.

| 저장 위치 | 저장 데이터 |
|---|---|
| `SavedTimetables.AssignmentsJson` | 최종 시간표 배정: 과목 ID, 요일, 교시, 강의실 ID |
| `SavedTimetables.SnapshotJson` | 저장 당시의 교과목/교수/강의실/Cross/재수강 설정 전체 |
| `SavedTimetableManualCrossLinks` | 수동 편집에서 유지한 Cross pair 세부 정보 |

따라서 저장 시간표는 단순히 격자 결과만 저장하는 것이 아니라, 그 시간표를 다시 표시하고 수정할 수 있도록 당시 설정 스냅샷과 수동 Cross 연결까지 함께 저장한다.
