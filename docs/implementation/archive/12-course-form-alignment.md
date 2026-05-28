# 12-course-form-alignment

## Goal
Improve readability of the "교과목 정보" editor in the Data Input screen by splitting the form into clear sections and aligning fields in consistent columns.

## Scope
- `wpf/TimetableScheduler.Wpf/Views/DataInputView.xaml` (`CourseGroupRowTemplate` only)

## Plan
1. Reorganize the base course fields (`이름`, `학년`, `시수/주`, `유형`, `교수 ID`, `학과`, `블록구조`) into a two-panel layout with shared label/input column widths.
   - Verify: field labels and editors are visually aligned in both panels and all existing bindings/converters remain unchanged.

2. Add subtle section headers to separate "기본 정보" and "운영 정보" while keeping existing style tokens.
   - Verify: section boundaries are visually clearer without introducing new theme resources.

3. Run WPF compile/test checks after XAML edits.
   - Verify: solution builds successfully and targeted tests pass (or report pre-existing environment constraints).

## Notes
- Do not change ViewModel property names, converters, or event handlers.
- Keep edits surgical: no unrelated layout refactors outside `CourseGroupRowTemplate`.

## Outcome
- Refactored the course fields into one strict shared-column grid (left/right pairs) so labels and inputs align on common vertical lines.
- Preserved all existing bindings and converters.
- Verification:
  - `dotnet build wpf/TimetableScheduler.slnx` passed.
  - `dotnet test wpf/TimetableScheduler.Tests/TimetableScheduler.Tests.csproj --filter "FullyQualifiedName~DataInput|FullyQualifiedName~ViewModel"` passed.
