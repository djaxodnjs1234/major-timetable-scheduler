# Manual save conflict warning

## Assumptions

- The requested warning applies to manual timetable save actions, including save-as-copy and save-before-export.
- Constraint violations should no longer block saving by themselves.
- Staged blocks that are not placed on the timetable remain a hard save blocker because the saved timetable would be incomplete.

## Plan

1. Inspect the manual edit save validation and dialog service.
   - Verify: identify the current hard-block path for validation conflicts.
2. Add a save-specific confirmation prompt that lists current violations and asks whether to continue saving.
   - Verify: rejecting the prompt cancels save, accepting the prompt saves.
3. Keep non-conflict save blockers intact.
   - Verify: staged blocks still prevent save.
4. Run focused ViewModel tests and WPF build, then archive this plan.
   - Verify: commands complete successfully.
