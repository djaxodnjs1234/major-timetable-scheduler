# Manual Fixed Course Delete Preview Isolation

## Goal

Deleting a year-fixed or school-fixed course block in Manual Edit must affect only the current manual edit timetable and must not remove the fixed course from other generated solution previews.

## Assumptions

- Year-fixed and school-fixed timetable cells are generated from source courses in the session snapshot.
- Manual Edit may use synthetic display assignments for school-fixed blocks, so the source course can appear "unused" if cleanup checks only visible assignment course IDs.
- User-created manual blocks may still be removed from the session snapshot when they are no longer referenced.

## Plan

1. Inspect Manual Edit deletion and cleanup paths.
   - Verify: identify where source courses are removed from the session data.
2. Add a regression test for deleting a fixed display block without mutating preview/source courses.
   - Verify: the test fails before the fix or directly covers the bug path.
3. Restrict cleanup so fixed source courses are preserved while unused manual-created courses can still be cleaned up.
   - Verify: focused Manual Edit tests pass.
4. Run the relevant WPF tests/build.
   - Verify: targeted tests and WPF project build complete successfully.
5. Archive this plan after implementation and verification.
   - Verify: the plan file is moved to `docs/implementation/archive/`.
