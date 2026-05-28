# 13-course-list-header-column-align

## Goal
Align the "교과목 정보 관리" list header rows (e.g., `GA1004 자료구조 2학년 4h (A·B분반)`) into fixed columns for better scan readability.

## Scope
- `wpf/TimetableScheduler.ViewModel/Pages/DataInputViewModel.cs`
- `wpf/TimetableScheduler.Wpf/Views/DataInputView.xaml` (`CourseGroupRowTemplate` header only)

## Plan
1. Replace single-string header rendering with structured header fields in `CourseGroupItem`.
   - Verify: each header row can bind to code/name/grade/hours/section fields independently.

2. Update `RebuildCourseGroups()` to populate structured header fields for both fixed and non-fixed rows.
   - Verify: non-fixed rows show section summary, fixed rows show fixed marker, and existing `DisplayLabel` compatibility remains.

3. Convert `Expander.Header` in `CourseGroupRowTemplate` to a fixed-column grid.
   - Verify: rows visually align by the same vertical column boundaries regardless of text length.

4. Run build/tests and archive this plan file.
   - Verify: compile success, targeted test pass, plan moved to archive.

## Notes
- Keep existing commands/event handlers unchanged.
- Do not touch unrelated 교수/강의실 templates.

## Outcome
- Added structured header fields on `CourseGroupItem`: `HeaderCode`, `HeaderName`, `HeaderGrade`, `HeaderHours`, `HeaderSectionInfo`.
- Updated `RebuildCourseGroups()` to populate those fields for fixed and non-fixed groups while keeping `DisplayLabel`.
- Replaced `Expander.Header` single label with fixed-column `Grid` in `CourseGroupRowTemplate` so rows align by column.
- Verification:
  - `dotnet build wpf/TimetableScheduler.Wpf/TimetableScheduler.Wpf.csproj -p:OutDir=C:\github\major-timetable-scheduler\wpf\_verify_out\` passed.
  - `dotnet test wpf/TimetableScheduler.Tests/TimetableScheduler.Tests.csproj --filter "FullyQualifiedName~DataInput|FullyQualifiedName~ViewModel"` passed.
  - `dotnet wpf/_verify_out/TimetableScheduler.Wpf.dll` ran without startup crash during smoke window.

## Spacing Tweak (follow-up)
- Widened header column widths in `CourseGroupRowTemplate` to improve readability from screenshot feedback:
  - `HeaderCode`: 92 -> 104
  - `HeaderGrade`: 74 -> 96
  - `HeaderHours`: 54 -> 70
  - `HeaderSectionInfo`: 130 -> 170
- Re-verified after spacing tweak:
  - `dotnet build wpf/TimetableScheduler.ViewModel/TimetableScheduler.ViewModel.csproj` passed.
  - `dotnet build wpf/TimetableScheduler.Wpf/TimetableScheduler.Wpf.csproj --no-dependencies` passed.
  - `dotnet test wpf/TimetableScheduler.Tests/TimetableScheduler.Tests.csproj --filter "FullyQualifiedName~DataInput|FullyQualifiedName~ViewModel"` passed.

## Fluid Auto-Sizing Tweak (follow-up)
- Replaced fixed header widths with fluid sizing:
  - `Auto` + `SharedSizeGroup` for content columns (`Code/Name/Grade/Hours/Section`).
  - `*` spacer columns between content columns for adaptive expansion.
  - Enabled `Grid.IsSharedSizeScope="True"` on the course `ItemsControl` so all rows share aligned content-column widths.
- Per user request, no additional build/test run was performed for this last spacing behavior tweak.
