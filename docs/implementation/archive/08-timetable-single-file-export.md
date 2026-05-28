# 시간표 단건 파일 내보내기/가져오기

## 목표

시간표 1개를 제약조건 스냅샷까지 포함한 자급자족 `.db` 파일로 주고받을 수 있게 한다.
가져온 시간표는 4번(수동 편집) 화면에서 제약조건까지 완전하게 편집된다.

## 배경

- xlsx 불러오기/내보내기는 배치 정보 `(과목ID, 요일, 교시, 강의실)`만 담아 제약조건이 없음.
- xlsx로 불러온 시간표를 수동 편집하면 제약조건(시수·교수·고정 등)이 불완전.
- `SavedTimetableRecord`는 이미 `SnapshotJson`(과목/교수/강의실/Cross 정의 전체)을 가짐.
  → 시간표 1건 + 스냅샷을 파일로 내보내면 자급자족.

## 설계

- **내보내기**: 시간표 1개 선택 → 폴더만 고르면 `시간표이름.db` + `시간표이름.xlsx`
  두 파일을 같은 이름으로 생성.
  - `.db` = `SavedTimetables` 테이블에 행 1개(`AssignmentsJson` + `SnapshotJson`)만 담은 SQLite 파일.
  - `.xlsx` = 기존 `FormattedTimetableExporter` 재사용 (사람용 출력물).
- **가져오기**: `.db` 파일만. 그 안의 시간표 행을 읽어 워크스페이스 `SavedTimetables`에
  **새 Guid ID**로 추가 (이름 충돌해도 별도 항목으로 공존).
- UI: 시간표 선택 화면 좌측 헤더에 기존 Excel 버튼과 별도로 시간표 파일 버튼 추가.

## 단계

### 1. SqliteRepository — 단건 파일 read/write
- `ExportSingleTimetable(SavedTimetableRecord record, string dbPath)`:
  새 SQLite 파일 생성 → `SavedTimetables` 스키마만 만들고 그 행 1개 insert.
- `ImportSingleTimetable(string dbPath)`: 그 파일 열어 `SavedTimetables` 행들을 읽어 반환
  (`List<SavedTimetableRecord>` — 보통 1개).
- verify: 단위 테스트 — export 후 import 하면 같은 record(SnapshotJson 포함) 복원.

### 2. WorkspaceService — 내보내기/가져오기 메서드
- `ExportTimetableFile(SavedTimetableRecord record, string dbPath)` → repo 호출.
- `ImportTimetableFile(string dbPath)`: repo로 읽어온 record들을 **새 Id로** `SaveTimetable`
  경유 저장 (SnapshotJson 보존). 컬렉션에 추가.
- verify: 단위 테스트 — 가져온 시간표가 SavedTimetables에 추가되고 SnapshotJson 유지.

### 3. TimetableSelectionViewModel — 커맨드
- `ExportTimetableFileCommand(string folderPath)`: 선택 시간표를 `폴더/이름.db` +
  `폴더/이름.xlsx`로 내보내기. `CanExecute = SelectedTimetable != null`.
- `ImportTimetableFileCommand(string dbPath)`: `.db` 가져오기, 가져온 첫 항목 선택.
- verify: 빌드.

### 4. TimetableSelectionView — UI 버튼
- 좌측 헤더에 기존 Excel 불러오기/내보내기와 구분되는
  `시간표 가져오기` / `시간표 내보내기` 버튼 추가.
- 코드비하인드: 폴더 선택 다이얼로그(내보내기) / `.db` OpenFileDialog(가져오기).
- verify: 앱 실행해 버튼 동작 확인.

### 5. 검증
- `dotnet build` + `dotnet test` 통과.
- 앱 실행: 시간표 내보내기 → .db+.xlsx 생성 확인 → 다른 이름 흉내내 가져오기 →
  목록 추가 → 편집(4번 화면)에서 제약조건 보이는지 확인.

## 결정 사항

- 내보내기는 .db + .xlsx 동시 생성, 가져오기는 .db만.
- 단건 .db는 메인 DB와 동일 SQLite 포맷 (SqliteRepository 재사용).
- 가져온 시간표는 새 Guid ID — 이름 충돌해도 공존.
- 내보내기 시 폴더만 선택, 파일명은 시간표 이름으로 자동.
- xlsx 불러오기/내보내기 기존 기능은 그대로 유지 (사람용 출력물).

## 검증 결과 (완료)

- 전체 솔루션 클린 빌드: 오류 0 (XAML 포함).
- 신규 테스트 4개 전부 통과:
  - `ExportSingleTimetable_ImportSingleTimetable_RoundTripsWithSnapshot`
  - `ExportSingleTimetable_OverwritesExistingFile`
  - `ExportTimetableFile_ImportTimetableFile_RoundTripsWithFreshId`
- 앱 정상 실행 — 시간표 선택 화면 새 버튼 4개(Excel/시간표 파일) 바인딩 오류 없음.
- 무관한 기존 실패 9개: `개설강좌 편람.xlsx` 위치 문제 (`FindRepoRoot`).

## 구현 메모

- 단건 .db 커넥션은 `Pooling=false` — 디스폰 즉시 파일 핸들 해제되어
  내보낸 파일을 바로 옮기거나 덮어쓸 수 있음. (메인 DB 풀에는 영향 없음.)
- 폴더 선택은 .NET 8 `Microsoft.Win32.OpenFolderDialog`.
