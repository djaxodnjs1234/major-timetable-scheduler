# 29. Solver UNKNOWN Timeout Options

## Goal

Expose the existing per-solve-attempt time limit as an advanced option in the WPF automatic generation panel, disabled by default, and explain `UNKNOWN` as a time/search-limit result.

## Assumptions

- Default behavior must remain unchanged unless the user enables the advanced setting.
- `DiverseSolverOptions.PerSolveTimeSec` already defaults to 5 seconds; the UI should preserve that default.
- `UNKNOWN` should not be shown as a normal completion when no previewable solution exists.

## Steps

1. Add ViewModel properties for the advanced setting.
   - Verify: default `UseAdvancedPerSolveTimeSec == false` and `PerSolveTimeSec == 5` in a ViewModel test.

2. Wire `PerSolveTimeSec` into `DiverseSolverOptions` only when the advanced setting is enabled.
   - Verify: existing solve tests still pass and default behavior remains unchanged.

3. Replace `UNKNOWN`/zero-solution status text with Korean timeout guidance.
   - Verify: status text tells users to enable/increase “한 번 탐색 제한 시간”.

4. Add the advanced checkbox/textbox and tooltip to the automatic generation panel.
   - Verify: the textbox is disabled by default and enabled only when the checkbox is on.

5. Run focused tests and WPF build.
   - Verify: `CourseGroupsTests` pass and WPF build exits with 0 errors.
