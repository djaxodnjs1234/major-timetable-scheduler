# WPF Configurable Lunch Policy

## Goal

Implement the lunch-policy rules from `docs/웹이전_제약조건및솔버동작_설계문서.md`
in the WPF product before any web migration work. The WPF solver, validation,
manual editing, saved timetable snapshots, rendering, and Excel export must all
use the same policy and the same per-day lunch decisions.

## Scope Decisions

- Change only the WPF implementation and shared WPF documentation. Do not add or
  modify web application code.
- Support the three required modes:
  - `BAN_4`: period 4 is lunch and cannot contain a class.
  - `BAN_5`: period 5 is lunch and cannot contain a class.
  - `BAN_AT_LEAST_ONE_OF_4_5`: exactly one of periods 4 and 5 is lunch for the
    whole timetable on each day.
- Use the design document's recommended `== 1` rule for the flexible mode. Do
  not interpret it as a separate lunch choice per professor, grade, or course.
- Keep `BAN_5` as the default for existing databases and saved timetable
  snapshots so current WPF behavior remains compatible.
- Do not expose `NONE` in this change. It is optional in the reference document
  and can be added as a separate product decision later.
- Treat the policy as one setting per WPF database/workspace. Do not implement
  the complete web `ConstraintSettings` contract as part of this change.
- Preserve the solver-selected lunch period per day during manual editing. A
  manual edit may not silently change the generated lunch decision.
- Do not delete or rewrite fixed slots when the user changes the policy. Keep
  the input and report a policy-specific validation error so the user can fix it.
- Let the user choose the lunch policy in the WPF input screen before starting
  generation. Do not hide the selection inside a config file or database-only
  setting.
- Render periods 4 and 5 with the same row height, borders, and individual cell
  structure as periods 1 and 2. Do not merge lunch cells across the week, a day,
  grades, or parallel sub-columns. Mark the actual lunch cells with `점심` while
  keeping the normal period and time labels.
- Update the existing Excel export in manual edit. Do not restore or add Excel
  export to the saved-timetable selection screen.

## Current WPF Assumptions To Replace

- `SchedulePeriods.LunchPeriod`, `SchedulePeriods.Daytime`, and
  `SchedulePeriods.Instructional` assume period 5 is always unavailable.
- `Constants.ValidPeriods`, `Constants.DaytimePeriods`, and
  `Constants.Len2StartPeriods` are global lists built around period 5 lunch.
- HC-03, HC-12, block construction, HC-19, academic time bands, grouping rules,
  and soft-constraint loops use those global lists.
- Input diagnostics, fixed-slot editors, professor-unavailable pickers,
  `ConflictDetector`, and manual-edit move/drop validation directly reject
  period 5.
- Result cards, timetable controls, and Excel export render one merged period 5
  lunch row.
- `AppData` and saved timetable records do not store a schedule policy or the
  solver's per-day flexible lunch choice.

## Deletion, Modification, and Retention Review

Delete:

- The universal `LunchPeriod = 5` scheduling decision and global period lists
  that remove period 5 before a policy is known.
- The universal two-hour start list derived from a permanent period 5 lunch.
- Dedicated `LunchRowHeight`/`LunchRowMinHeight` dependency properties and their
  XAML values.
- Full-week or per-day merged lunch-border helpers in timetable controls and
  Excel output.
- Result-card behavior that replaces an entire period 5 row with one lunch bar.
- Raw period-5 skip branches in solver, diagnostics, fixed-slot input, manual
  editing, rendering, and export.

Modify:

- HC-12 and every period loop affected by a candidate lunch period.
- Block candidate generation, two-hour pair starts, fixed slots, professor
  unavailable slots, Cross/retake checks, capacity checks, and graduate daytime
  overflow.
- Solver results, ranking, saved records, and session handoff so the selected
  per-day lunch cell is not lost.
- Input, conflict, and generation diagnostics so messages identify the selected
  policy and the actual conflicting period/day.
- Grid cells, result previews, saved previews, manual edit, and Excel export to
  mark lunch in ordinary unmerged cells.
- Stale tests that still require the removed saved-timetable selection export.
  The current product scope keeps export in manual edit only.

Keep:

- Period numbering `1..13`, the period-to-time formula, night periods `10..13`,
  and the separator before night classes.
- The full `x`/`y` variable grid. Policy constraints may zero a lunch cell; the
  variable model itself does not need to be redesigned.
- Room, professor, grade, section, Cross, retake, fixed-time, and school-fixed
  semantics other than replacing their period iteration source.
- Existing assignment rows in the hidden Excel data sheet; lunch is schedule
  metadata, not a fake course assignment.

## Implementation Plan

1. Add the domain policy and backward-compatible persistence.
   - Add a WPF domain `LunchPolicyMode` and a small immutable `SchedulePolicy`
     model with `BAN_5` as its default.
   - Add the policy to `AppData`, `WorkspaceService.Snapshot()`,
     `SchedulingSnapshot()`, session workspaces, and solver cloning.
   - Add a single-row settings table or equivalent JSON setting in
     `SqliteSchema`/`SqliteRepository`; missing settings in an older database
     must load as `BAN_5`.
   - Include the policy in `SavedTimetableRecord.SnapshotJson`. Older snapshot
     JSON without the new field must still resolve as `BAN_5`.
   - Verify: repository and snapshot round-trip tests preserve all three modes,
     while legacy database and legacy snapshot tests load `BAN_5` without data
     loss.

2. Centralize all derived period and block rules.
   - Add one pure policy helper usable by Solver, ViewModel, and Data without a
     WPF UI dependency.
   - Derive static blocked periods, instructional periods, daytime periods,
     academic-level periods, lunch-candidate checks, and valid block starts
     from the selected policy.
   - Derive two-hour starts from consecutive allowed runs instead of using the
     universal `{1,3,6,8,10,12}` list. Flexible mode must expose starts that are
     valid under at least one daily lunch choice and identify which candidate
     period must remain open.
   - Keep all-period and night-period definitions unchanged.
   - Verify: focused domain/solver-helper tests cover `BAN_4`, `BAN_5`, and both
     possible daily choices in flexible mode, including `3~4`, `4~5`, and
     `5~6` blocks.

3. Make every solver phase use the policy.
   - Pass the schedule policy into every `ModelBuilder.Build` call in phases
     1A, 1B, 1C, and 2.
   - Extend `BuildResult` with flexible lunch decision variables for periods 4
     and 5 on each day.
   - Replace HC-12 with policy-driven constraints. In flexible mode, enforce
     exactly one banned candidate per day and link every course assignment at
     periods 4 and 5 to that decision.
   - Generate HC-06 block starts from policy candidates and add implications
     from starts containing period 4 or 5 to the matching flexible lunch
     decision. Never allow a flexible block containing both 4 and 5.
   - Update HC-01/02/03/04/08/11/14/16/17/19/21/22/23/24 and relevant soft
     constraint loops to use policy-derived candidate periods. Professor
     unavailable period 4 or 5 must remain effective when that period is open.
   - Update undergraduate capacity and graduate daytime overflow calculations
     to use policy-filtered daytime periods.
   - Verify: solver tests prove no class uses the static lunch period; flexible
     mode leaves exactly one candidate empty globally per day; fixed period 4
     forces period 5 lunch; fixed period 5 forces period 4 lunch; fixed use of
     both candidates on one day is infeasible; and legacy `BAN_5` results remain
     valid.

4. Carry each solution's lunch decisions through ranking and saving.
   - Introduce a solver solution value that contains assignments plus the
     selected lunch period for every day. Extract flexible lunch variable values
     from CP-SAT; produce the deterministic static map for `BAN_4`/`BAN_5`.
   - Include lunch decisions in solution uniqueness so two solutions with the
     same assignments but different empty-day lunch choices are not silently
     collapsed.
   - Preserve this metadata through `DiverseSolverResult`, scoring/ranking,
     result selection, and the Results-to-ManualEdit handoff.
   - Add saved-timetable storage for the per-day lunch map, with a migration and
     a `BAN_5` fallback for older rows.
   - Verify: ranking does not detach lunch metadata from assignments, and saving,
     reopening, copying, and resaving a timetable preserve both its policy
     snapshot and daily lunch choices.

5. Add the WPF setting and policy-aware input validation.
   - Add a lunch-policy selector under `DataInputView`'s generation settings,
     visible before the Generate action, with clear Korean labels and time
     ranges.
   - Persist a selection through `WorkspaceService`; changing it must refresh
     policy-dependent editors and validation without clearing course data.
   - Make `TimeSlotPickerViewModel` disable only the statically banned period.
     Flexible mode must allow professors to select both periods 4 and 5 as
     unavailable times.
   - Make `FixedSlotEditorViewModel` use policy-derived block starts. Flexible
     mode may select period 4 or 5 individually but must not offer a block that
     contains both.
   - Replace `DataInputViewModel` and `TimetableDiagnostics` period-5 checks with
     policy-specific checks and Korean messages for static-lunch conflicts,
     flexible fixed-slot conflicts, invalid blocks, and exhausted daily lunch
     choices.
   - Verify: ViewModel tests cover selector persistence, editor options, no
     silent fixed-slot mutation, policy-specific IE/GE diagnostics, and
     professor-unavailable behavior in flexible mode.

6. Apply the generated policy snapshot to conflict detection and manual editing.
   - Pass the session `SchedulePolicy` and saved per-day lunch map into
     `ConflictDetector` and every manual-edit validation path.
   - Replace direct lunch checks in move, swap, staged-block placement, drag/drop,
     cross placement, full validation, and two-hour-start validation with the
     shared helper.
   - Freeze the generated timetable's daily lunch map for the edit session.
     Reject edits targeting the saved lunch cell even if the current global
     workspace policy later changes.
   - Keep room, professor, grade, section, Cross, retake, fixed-time, and
     academic-band conflict checks active on the open candidate period.
   - Verify: manual-edit tests cover all three modes, both flexible daily choices,
     block placement around periods 4/5, undo/redo, save/copy/reopen, and
     independence from later global policy changes.

7. Render and export the same lunch decisions.
   - Give timetable/grid ViewModels policy-aware lunch-cell information instead
     of deriving `IsLunch` from `Period == 5`.
   - Remove special lunch-row heights and render every period row with the same
     sizing rules as ordinary class rows.
   - For `BAN_4` and `BAN_5`, keep the normal period/time label and mark each
     configured day/grade/sub-column cell as lunch without merging cells.
   - For flexible mode, keep normal period/time labels and mark only the
     solver-selected cells for each day. The other candidate period remains a
     normal class cell. Do not merge cells within a day or across the week.
   - Update result-card mini previews so `MiniPreviewCell.IsLunch` works per day;
     do not hide an entire row in flexible mode.
   - Pass the policy and daily lunch map into `FormattedTimetableExporter`.
     Give every period the ordinary row height and preserve every visible cell.
     Write `점심` into the selected cells instead of merging a lunch range.
   - Update the existing manual-edit Excel export to use the session's saved
     policy/lunch map. Do not add another export entry point.
   - Verify: WPF control tests and ClosedXML tests cover `BAN_4`, `BAN_5`, mixed
     flexible days, course rendering in the open period, row heights/borders,
     unmerged lunch cells, and export from manual edit.

8. Complete regression verification and documentation.
   - Add or update tests in `TimetableScheduler.Tests` and
     `TimetableScheduler.Wpf.Tests` without weakening existing HC, snapshot,
     manual-edit, or export coverage.
   - Run:
     - `dotnet test wpf/TimetableScheduler.Tests/TimetableScheduler.Tests.csproj`
     - `dotnet test wpf/TimetableScheduler.Wpf.Tests/TimetableScheduler.Wpf.Tests.csproj`
     - `dotnet build wpf/TimetableScheduler.slnx`
   - The pre-implementation baseline currently does not compile because stale
     tests still reference the intentionally removed saved-timetable selection
     export. Remove those obsolete tests; do not reintroduce the removed product
     feature.
   - Manually smoke-test one timetable per mode: change the policy, generate,
     inspect previews, enter manual edit, save/reopen, and export to Excel.
   - Update `wpf/README.md`, `docs/hc_sc_설정값.md`,
     `docs/시간표생성_엔진로직.md`, and `docs/오류_식별번호.md` so HC-12 and the
     related UI/diagnostic behavior no longer claim period 5 is universally
     blocked.
   - Verify: repository search finds no production lunch decision based on a raw
     `period == 5`, `period != 5`, or the legacy global two-hour-start list.
     Constants that describe period numbering or time labels may remain.

## Expected Main File Groups

- Domain and persistence: `TimetableScheduler.Domain/SchedulePeriods.cs`, new
  policy model/helper files, `TimetableScheduler.Data/AppData.cs`,
  `SqliteSchema.cs`, `SqliteRepository.cs`, and saved-timetable models.
- Solver: `Constants.cs`, `ModelBuilder.cs`, `BasicHcs.cs`, `BlockHcs.cs`,
  `GroupingHcs.cs`, `AcademicLevelTimePolicy.cs`, `SoftConstraints.cs`,
  `DiverseSolver.cs`, `ConflictDetector.cs`, and `TimetableDiagnostics.cs`.
- ViewModel: `WorkspaceService.cs`, `SolverService.cs`, data input editors,
  `DataInputViewModel.cs`, results/selection models, grid models, and
  `ManualEditViewModel.cs`.
- WPF/Data presentation: `DataInputView.xaml`, timetable controls,
  `ResultsView.xaml`, and `FormattedTimetableExporter.cs`.
- Tests and documentation listed in step 8.

## Completion Criteria

- The selected WPF lunch policy is persisted and included in generation and
  saved timetable snapshots.
- All solver phases, input validation, conflict detection, and manual editing
  enforce the same policy.
- Flexible mode keeps exactly one of periods 4 and 5 empty for the whole
  timetable on every day, and its solver choice survives ranking and saving.
- WPF previews, full timetables, reopened saved timetables, manual edit, and
  Excel exports display the same lunch cells.
- Legacy databases and saved timetables continue to behave as `BAN_5`.
- Both test projects pass and the WPF solution builds successfully.

## Approval Gate

Do not start production-code implementation until the user reviews this complete
scope and explicitly approves proceeding. After approval, implement the plan as
one coordinated change and return only for a genuine new product decision or an
external blocker.
