1. Refactor badge creation in `UnifiedTimetableControl` into a shared helper.
   - Verify: MouseEnter still uses the same evaluator results and creates the same buttons.
2. Call the shared helper during course-cell DragOver when the drag source and target are current, occupied, and different.
   - Verify: normal cell DragOver effects remain based on the existing DropMoveEvaluator.
3. Run the WPF build and ViewModel test suite.
   - Verify: both requested dotnet commands complete successfully.
