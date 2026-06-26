# Manual Edit True Back Navigation

## Goal

Make the manual edit page's Back command return to the actual previous page. In particular, after entering constraint editing from manual edit and then returning to manual edit with "수동편집으로", Back should go to the constraint-edit input page instead of the original timetable selection page.

## Assumptions

- Existing direct routes should remain unchanged:
  - Saved timetable selection -> manual edit -> Back returns to selection.
  - Results -> manual edit -> Back returns to results.
- The special case is manual edit -> constraint input -> manual edit -> Back should return to that input page.

## Steps

1. Inspect current MainWindow navigation state.
   - Verify: identify how `_manualBackTarget` and `_inputBackTarget` are set.
2. Update the manual edit return path to preserve the immediate previous input page.
   - Verify: add a MainWindowViewModel navigation regression test.
3. Run focused MainWindow navigation tests.
   - Verify: existing direct back behavior and new true-back behavior pass.
4. Archive this plan.
   - Verify: move this file to `docs/implementation/archive/`.
