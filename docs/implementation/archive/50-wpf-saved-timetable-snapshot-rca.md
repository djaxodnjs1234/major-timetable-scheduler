# WPF saved timetable snapshot RCA

## Steps

1. Trace all runtime `SaveTimetable` callers in WPF and record the snapshot source passed to `WorkspaceService.SaveTimetable`.
   - Verify: every save path is identified with its exact `AppData` source.

2. Read the repository and schema code that persists `SavedTimetableRecord.SnapshotJson`.
   - Verify: the storage column, insert/update path, and migration behavior are clear.

3. Read the load-side resolver that rehydrates saved timetable snapshots.
   - Verify: note where missing metadata can be filled from live workspace data.

4. Summarize the root cause candidates in concise bullets with file paths and functions.
   - Verify: the final note distinguishes save-time snapshot creation from load-time handling.
