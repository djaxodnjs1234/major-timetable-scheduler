# Lunch-aware fixed time inputs and manual zoom connector stability

## Goal

Prevent users from selecting fixed-time slots that cannot be valid under the
current lunch policy, including lunch policies that involve period 4 and period
5. Also fix the manual-edit issue where block error connector lines break or
become misaligned after timetable zoom changes.

## Assumptions

- This task targets the WPF product first. The Python prototype has a similar
  fixed-schedule editor, but the current lunch-policy work and manual-edit zoom
  issue are in `wpf/`.
- "Fixed time related" means both fixed course block start selection and any
  shared time-slot picker that marks lunch cells as unavailable.
- For static lunch modes, the exact lunch period is disabled:
  - `BanPeriod4`: period 4 is not selectable.
  - `BanPeriod5`: period 5 is not selectable.
- For flexible lunch mode, one of periods 4 or 5 must remain lunch for each day.
  A fixed block may use period 4 or period 5 individually, but a block that
  covers both 4 and 5 must not be offered because it leaves no lunch candidate.
- Manual editing remains permissive: zoom-line stability is a rendering fix, not
  a change to conflict rules.

## Plan

1. Confirm every fixed-time input surface that needs lunch-policy filtering.
   - Inspect `FixedSlotEditorViewModel`, `FixedSlotEditorControl`,
     `TimeSlotPickerViewModel`, and the Data Input refresh path.
   - Verify: list the exact ViewModel methods that produce fixed-time period
     options and lunch-disabled cells before editing.

2. Make fixed course block start options use the shared schedule-policy helper.
   - Replace ad hoc block-start generation in `BlockSlotEntry.BuildPeriodOptions`
     with `SchedulePolicyRules.PossibleBlockStarts(...)`.
   - Keep graduate/night-course behavior unchanged unless existing policy helpers
     already define a different valid period set.
   - Verify: `BanPeriod4` does not offer starts whose block includes period 4;
     `BanPeriod5` does not offer starts whose block includes period 5; flexible
     mode does not offer any block that includes both periods 4 and 5.

3. Keep the selected value valid when policy or block size changes.
   - When rebuilding fixed-slot editors, if an existing selected start is no
     longer in the option list, choose the first valid option instead of leaving
     an invisible or invalid combo value.
   - Preserve valid existing fixed slots so policy changes do not silently clear
     user data.
   - Verify: changing lunch policy refreshes the period options and does not
     mutate fixed slots except for the UI fallback value shown in the editor.

4. Align generic time-slot picker lunch-disabled behavior with policy cases.
   - Keep `TimeSlotPickerViewModel` using `SchedulePolicyRules.IsStaticallyBlocked`
     for static modes.
   - Confirm flexible mode intentionally leaves both 4 and 5 selectable for
     professor unavailable times, because the generated lunch choice is not known
     during input.
   - Verify: focused tests cover static period 4, static period 5, flexible mode,
     and `None`.

5. Strengthen input validation for fixed slots that still become invalid.
   - Update existing fixed-slot validation and diagnostics to catch static lunch
     conflicts and flexible blocks that consume both 4 and 5.
   - Use policy-specific Korean messages so the user knows whether period 4,
     period 5, or the 4~5 flexible lunch pair caused the problem.
   - Verify: generation pre-checks produce a clear error for invalid imported or
     previously saved fixed slots.

6. Reproduce and isolate the manual-edit zoom connector break.
   - Inspect `UnifiedTimetableControl.AddConflictConnectorOverlay` and how
     `ManualEditView` applies `LayoutTransform` through `TimetableZoom.Shared`.
   - Reproduce by opening manual edit with conflict connectors, then changing
     zoom between 50%, 100%, and 200%.
   - Verify: before fixing, record whether the issue is stale canvas geometry,
     delayed dispatcher timing, stroke scaling, clipping, or stale block-border
     references after rebuild.

7. Make conflict connectors recalculate after zoom/layout changes.
   - Move connector drawing behind a small redraw method that clears previous
     paths and recalculates coordinates after layout is complete.
   - Hook the redraw to layout completion or size changes for the root grid and
     block borders, so zoom changes cannot leave stale connector geometry.
   - Keep connector opacity, label tooltip, and conflict data unchanged.
   - Verify: connectors remain attached to the correct blocks at minimum,
     default, and maximum zoom.

8. Add focused regression coverage.
   - Add ViewModel tests for fixed-time period options under all lunch modes.
   - Add WPF/source-level tests for connector redraw wiring where practical.
   - Add a manual QA note if the connector issue requires visual confirmation.
   - Verify: tests fail before implementation where possible and pass after.

9. Run verification and archive only after implementation is complete.
   - Run `dotnet test wpf/TimetableScheduler.Tests/TimetableScheduler.Tests.csproj`.
   - Run `dotnet test wpf/TimetableScheduler.Wpf.Tests/TimetableScheduler.Wpf.Tests.csproj`.
   - Run `dotnet build wpf/TimetableScheduler.slnx`.
   - Manually smoke-test fixed-time selection and manual-edit zoom.
   - Verify: all relevant checks pass or any unrelated environment failures are
     documented, then move this file to `docs/implementation/archive/`.

## Success Criteria

- Fixed-time period options cannot select lunch-invalid starts for the active
  policy.
- Static period 4, static period 5, flexible 4/5, and no-lunch modes are all
  covered by focused tests.
- Existing valid fixed slots survive editor rebuilds and policy refreshes.
- Manual-edit red connector lines remain visually continuous and anchored to
  their blocks across zoom changes.
- No solver or conflict semantics are changed beyond preventing invalid fixed
  time input and reporting invalid imported/saved fixed slots clearly.
