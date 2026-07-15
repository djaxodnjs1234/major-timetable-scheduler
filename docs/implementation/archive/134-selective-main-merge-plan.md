# Selective main merge into lunchtime_v2

## Current State
- Branch: `lunchtime_v2`.
- A merge attempt from `origin/main` was started and currently has conflicts.
- Do not resolve conflicts yet in this planning pass.
- Desired direction: keep `lunchtime_v2` as the source of truth and only port useful `main` changes.

## Goal
Merge `main` selectively into `lunchtime_v2` while preserving the current branch behavior for lunch policy, manual editing, validation UI, solver diagnostics, and conflict display.

## Main Changes To Consider

### Bring Over
1. Expected enrollment data field
   - Source commit: `a9174a8 추가 수정`.
   - Files involved: `Course`, `DomainHelpers`, `XlsxLoader`, `SqliteSchema`, `SqliteRepository`, export/persistence clone paths, and related UI display.
   - Reason: this is additive data support (`ExpectedEnrollment`, Excel `수강 인원`) and should not conflict conceptually with current lunch/manual-edit work.

2. Any small persistence/export compatibility changes required by the expected enrollment field
   - Keep only schema/repository/export changes that are needed to store, load, and clone the new field.
   - Avoid taking unrelated UI layout or validation changes from the same commit unless they are required.

### Bring Over Now
1. Professor first-period soft constraint (`SC04`)
   - Source commit: `6094ee6 교수 1교시 제한 sc 추가`.
   - Files involved: `SoftConstraints`, `DiverseSolver`, `SolutionScoring`, result display, and tests.
   - Decision: bring this into `lunchtime_v2`.
   - Merge rule: preserve current branch solver/lunch behavior and add SC04 as an additional soft-constraint phase/score.

### Optional / Confirm Before Bringing Over
1. Main's manual validation report service
   - Source commit: `0b1d730 제약조건 검증 추가`.
   - Files involved: `ManualEditViewModel`, `IConflictDialogService`, `ManualValidationReport`, `MessageBoxConflictDialogService`.
   - Reason to bring: may contain reusable report formatting.
   - Reason to skip: current branch already has a richer full-validation UI with tooltips, error-only handling, concise details, retake/fixed-time/professor-unavailable improvements.
   - Recommendation: skip wholesale; only inspect for any missing check that current branch does not already cover.

### Skip By Default
1. Warning wording changes from `0990003 경고 내용 수정`
   - Current branch intentionally removed yellow/warning handling and treats validation issues as errors.
   - Do not reintroduce warning-oriented wording.

2. Main's overlapping manual edit validation UI
   - Current branch has newer behavior for right-side constraint display, full validation grouping, fixed-time original position, retake details, and professor-unavailable names.
   - Resolve conflicts in favor of `lunchtime_v2` unless a `main` change is clearly additive and non-overlapping.

3. Main UI layout chunks that duplicate or regress current manual-edit controls
   - Keep current branch layout for manual block controls, zoom behavior, violation line rendering, and validation dialog text.

## Conflict Resolution Strategy
1. Preserve `lunchtime_v2` for heavily edited behavior files first.
   - Primary ours-first files:
     - `wpf/TimetableScheduler.ViewModel/Pages/ManualEditViewModel.cs`
     - `wpf/TimetableScheduler.Wpf/Views/ManualEditView.xaml`
     - `wpf/TimetableScheduler.Wpf/Services/MessageBoxConflictDialogService.cs`
     - `wpf/TimetableScheduler.ViewModel/Services/IConflictDialogService.cs`
     - `wpf/TimetableScheduler.Solver/ConflictDetector.cs`
     - `wpf/TimetableScheduler.Solver/DiverseSolver.cs`

2. Manually port selected additive changes from `main`.
   - Port `ExpectedEnrollment` model/storage/import/export support.
   - Port SC04 while preserving current branch solver/lunch behavior.
   - Do not port `ManualValidationReport` unless a missing validation item is identified.

3. Check for missing main-only behavior after conflict markers are removed.
   - Compare `origin/main..HEAD` by feature, not by file.
   - Confirm current branch still has:
     - lunch-aware fixed time and block placement
     - manual block staging/add/edit/delete rules
     - error-only validation display
     - full validation tooltips and concise details
     - fixed-time original-position text
     - retake block-level details
     - professor-unavailable offending-professor-only display

4. Verify after actual merge work.
   - Build: `dotnet build wpf/TimetableScheduler.slnx --no-restore`.
   - Targeted tests:
     - lunch policy tests
     - manual edit validation tests
     - expected enrollment persistence/import tests if the field is ported
   - Full tests only after targeted checks pass; known workbook-path tests may still fail if `개설강좌 편람.xlsx` is unavailable.

## Decision
- Bring `SC04` professor first-period soft constraint into `lunchtime_v2`.
- For ambiguous overlap, prefer the current branch behavior.
