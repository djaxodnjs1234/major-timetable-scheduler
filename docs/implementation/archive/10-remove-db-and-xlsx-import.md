# 파일 기능 정리 — xlsx 내보내기만 유지

## 목표

시간표 선택 화면에서 `.db` 내보내기/가져오기와 `xlsx 불러오기`를 제거하고
`xlsx 내보내기`만 남긴다.

## 배경

- `.db` 단건 내보내기/가져오기는 "외부에서 받은 시간표를 제약조건까지 편집"용이었으나,
  단일 PC·단일 사용자 흐름에서는 불필요. 정상 흐름(2→3→4번)으로 저장된 시간표는
  메인 DB의 SnapshotJson으로 이미 자급자족.
- `xlsx 불러오기`는 제약조건이 빠진 채 들어와 4번 화면 편집이 부정확 → 제거.
- `xlsx 내보내기`는 사람이 보는 출력물로 가치 있음 → 유지.

## 제거 대상

### A. TimetableSelectionView.xaml — 버튼 4개 중 3개 제거
- `📄 Excel 불러오기` 제거, `📦 시간표 가져오기` 제거, `📦 시간표 내보내기` 제거.
- `📄 Excel 내보내기`만 유지 (라벨은 "내보내기"로 단순화).

### B. TimetableSelectionView.xaml.cs — 핸들러 제거
- `OnImportXlsxClick`, `OnExportTimetableFileClick`, `OnImportTimetableFileClick` 제거.
- `OnExportXlsxClick`, `OnDeleteClick` 유지.

### C. TimetableSelectionViewModel.cs — 커맨드 제거
- `ImportXlsxCommand`, `ExportTimetableFileCommand`, `ImportTimetableFileCommand` 제거.
- `OnSelectedTimetableChanged`의 `ExportTimetableFileCommand.NotifyCanExecuteChanged()` 제거.
- `ExportXlsxCommand` 유지.

### D. WorkspaceService.cs — 미사용 메서드 제거
- `ExportTimetableFile`, `ImportTimetableFile` 제거 (이번 변경으로 호출처 사라짐).
- `ImportFromXlsx`는 DataInputView(편람 불러오기)에서 쓰므로 **유지**.

### E. SqliteRepository.cs — 미사용 메서드 제거
- `ExportSingleTimetable`, `ImportSingleTimetable`, `StandaloneConnStr`,
  `SavedTimetablesDdl` 제거 (호출처 사라짐).

### F. 테스트 제거
- `SqliteRepositoryTests`: `ExportSingleTimetable_*` 2개 제거.
- `WorkspaceServiceTests`: `ExportTimetableFile_ImportTimetableFile_*` 1개 제거.

## 확인 필요

- `TimetableXlsxService.Import` — `ImportXlsxCommand` 제거 후에도 다른 호출처가 있는지 확인.
  없으면 `TimetableXlsxService`는 export만 쓰이게 됨 (Import 메서드는 일단 남겨둠 — 무관 정리).

## 단계

1. 호출처 grep으로 제거 안전성 확인 → verify: 미사용 확정.
2. A~F 순서로 제거 → verify: 빌드.
3. `dotnet build` + `dotnet test` → verify: 통과.
4. 앱 실행 → verify: 시간표 선택 화면에 "내보내기" 버튼만, 정상 동작.

## 결정 사항

- xlsx 내보내기만 유지. .db 기능과 xlsx 불러오기 전부 제거.
- 직전 커밋에서 추가한 `.db` 단건 기능을 되돌리는 작업 — surgical하게 추가분만 제거.

## 검증 결과 (완료)

- 전체 솔루션 클린 빌드: 오류 0.
- 테스트 154개 통과 — `.db` 단건 관련 테스트 3개 제거.
- 앱 정상 실행 — 시간표 선택 화면에 `📄 Excel 내보내기` + `+ 새 시간표 만들기`만.
- 무관한 기존 실패 9개: `개설강좌 편람.xlsx` 위치 문제 (`FindRepoRoot`).
- `ImportFromXlsx`(편람 불러오기)와 `TimetableXlsxService.Import`(테스트용)는 유지 — 별개 기능.
