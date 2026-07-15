# Manual edit whole-course delete option

## Assumptions

- The checkbox belongs next to the inspector's "delete selected block" button.
- It is unchecked by default, so existing behavior stays "delete only the selected block".
- When checked, deleting removes every timetable assignment and staged block with the selected block's same `CourseId`.

## Plan

1. Add a ViewModel checkbox state for whole-course deletion and reset it to unchecked by default.
   - Verify: the state is false on a fresh manual edit screen and after a delete action.
2. Update the delete command to branch between selected-block deletion and whole-course deletion.
   - Verify: unchecked deletion removes only the selected block; checked deletion removes all blocks with the same `CourseId` from the timetable and staging panel.
3. Add the checkbox next to the inspector delete button.
   - Verify: WPF build succeeds and the checkbox is bound two-way.
4. Add focused tests for whole-course deletion.
   - Verify: manual-edit tests pass.
5. Run focused tests and WPF build, then archive this plan.
   - Verify: test/build commands complete successfully.
