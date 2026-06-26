# Direct manual edit and constraint return

1. Open a saved timetable's manual editor directly from the selection screen.
   - Verify: the Edit button loads the saved assignments, constraint snapshot, name, and saved-timetable id without visiting data input.
2. Add a manual-editor command to edit constraints in the data-input screen.
   - Verify: data input opens in an isolated session seeded with the current manual assignments and constraint snapshot.
3. Return from data input to manual editing without discarding current manual assignments or Cross links.
   - Verify: applying the return command reloads manual editing with changed constraints and preserved working assignments; cancelling returns to the untouched manual state.
4. Add navigation regression tests and run the focused test suite.
   - Verify: direct edit, return, cancel, and save flows pass.
