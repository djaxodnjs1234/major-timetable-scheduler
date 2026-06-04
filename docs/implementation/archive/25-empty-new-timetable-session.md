1. Confirm the current source of prefilled data.
   - Verify: identify the global SQLite DB path and automatic XLSX import path used before entering the Data Input screen.
2. Make new timetable creation use an empty in-memory session workspace.
   - Verify: global DB courses remain untouched while `LoadForNewTimetable()` exposes zero courses/professors/rooms in the input screen.
3. Preserve session snapshots through solve/result/manual-edit flow.
   - Verify: result/manual screens receive `CurrentSnapshot()` whenever the input workspace is a session, not only for existing timetable edit mode.
4. Add regressions and run checks.
   - Verify: targeted ViewModel tests, solution build, full tests, and a minimal driver all pass.
