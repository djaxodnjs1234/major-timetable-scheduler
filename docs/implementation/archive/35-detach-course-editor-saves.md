# Detach course editor saves from workspace state

1. Store cloned course values in the single and batch course update paths.
   - Verify: changing the caller-owned edit object after save cannot mutate workspace courses.
2. Add a course-group regression test for professor IDs after a saved edit object is cleared.
   - Verify: saved section professor IDs remain intact.
3. Run the focused ViewModel tests and build the WPF application.
   - Verify: the updated behavior compiles and all selected tests pass.
