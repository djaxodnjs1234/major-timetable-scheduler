# Saved Cross Initial Restore Fix

1. Inspect saved timetable restore order and Cross cleanup paths.
   - Verify: identify where saved ManualCrossLinks are restored, where conflicts refresh, and where invalid Cross cleanup can run.
2. Fix only the restore path if saved links are over-validated or cleaned during initial load.
   - Verify: restored links are present before conflict detection and HC-11 exception callback sees them.
3. Add targeted tests for saved Cross load, refresh persistence, HC-20 non-exemption, post-edit cleanup, and save blocking without Cross.
   - Verify: ManualEditViewModel tests pass.
4. Run WPF build and full test suite, then archive this plan.
   - Verify: build/test commands succeed.
