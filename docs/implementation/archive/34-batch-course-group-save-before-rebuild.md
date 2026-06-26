# Batch course-group saves before rebuilding editors

1. Save every section of a course group before emitting the workspace changed event.
   - Verify: a group save emits one workspace change rather than one per section.
2. Use the batch update from the shared course-group save path.
   - Verify: changing shared course fields preserves each section's professor ID after rebuilding the input list.
3. Run the focused and non-Excel WPF core tests.
   - Verify: the regression test and existing tests pass.
