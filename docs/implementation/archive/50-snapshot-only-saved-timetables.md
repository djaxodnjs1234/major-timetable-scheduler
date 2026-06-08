## Goal

Restore snapshot-only behavior for saved timetables so reopening always reflects the saved-time state, and verify exactly what the current save action persists.

## Plan

1. Inspect the current save/reopen pipeline and identify every call site using live-workspace fallback.
   - Verify: list the save sources and the reopen paths for selection, existing-timetable edit, and manual edit.

2. Replace merge/fallback reopen behavior with snapshot-only deserialization.
   - Verify: reopened saved timetables use only `SnapshotJson`; legacy rows without a snapshot no longer pull professor/type from the current workspace.

3. Update tests to reflect snapshot-only policy and cover current save contents.
   - Verify: tests confirm save stores assignments plus snapshot metadata, and reopen no longer repairs blank fields from live workspace.

4. Build, run focused tests, and exercise the save/reopen flow manually.
   - Verify: save -> selection preview -> existing edit -> manual edit all show the saved-time state only.
