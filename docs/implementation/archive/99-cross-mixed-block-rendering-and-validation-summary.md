# Cross Mixed Block Rendering and Validation Summary Fix

## Goal

Keep Cross-linked blocks with different durations visible as separate timetable blocks, ensure Cross-linked blocks remain swappable, and restore the full validation button to show per-check normal/abnormal status rows instead of only violation groups.

## Steps

1. Reproduce and inspect rendering of Cross-linked one-hour and two-hour blocks.
   - Verify: a Cross link between different-duration sections does not collapse or hide either block.

2. Adjust manual timetable display identity/grouping so Cross display columns do not merge distinct block durations.
   - Verify: same-slot Cross blocks with different `RowSpan` values produce separate visible cells.

3. Confirm swap eligibility for Cross-linked visible blocks.
   - Verify: a selected block can swap with a Cross-linked target block.

4. Restore full validation summary behavior.
   - Verify: the full validation command lists validation categories with normal/abnormal status and keeps detailed violation information where appropriate.

5. Add or update focused tests and run relevant test/build checks.
   - Verify: ViewModel tests cover the regression paths and WPF/ViewModel projects compile.
