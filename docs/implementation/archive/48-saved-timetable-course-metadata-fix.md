## Goal

Fix the WPF saved-timetable flow so reopening a saved timetable preserves course-management professor/type metadata, and ensure course management visibly shows that metadata after whole-timetable save/load.

## Steps

1. Inspect whole-timetable save/load snapshot flow.
   - Verify: Confirm where `SavedTimetableRecord.SnapshotJson` is created, restored, and reused during re-save.

2. Add save/load regression coverage.
   - Verify: Tests fail if re-saving a loaded saved timetable loses professor or course-type metadata, or if reopening a saved timetable cannot display them in course management.

3. Implement the minimal snapshot/UI fix.
   - Verify: Re-saved timetables keep the original snapshot metadata, and course management visibly shows professor and type after reopening.

4. Validate targeted WPF/ViewModel behavior.
   - Verify: Save/load regression tests and the WPF DataInput UI regression test pass.

5. Archive this plan after verification.
   - Verify: This file is moved to `docs/implementation/archive/` when the task is complete.
