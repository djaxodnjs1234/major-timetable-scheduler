1. Confirm saved-timetable edit flow.
   - Verify: identify whether existing timetable edits use a session workspace and where saves write `SnapshotJson`.
2. Preserve the edited session snapshot when saving from manual edit.
   - Verify: saved timetable `SnapshotJson` uses the session snapshot when one exists, and keeps the global workspace snapshot for new timetable saves.
3. Add a regression test for existing timetable edit/save.
   - Verify: saving a manual edit loaded from a session snapshot stores session-only course/constraint data, not the global DB state.
4. Run targeted tests, build, and a minimal ViewModel driver.
   - Verify: automated checks pass and the create/edit/save/reload behavior works through the ViewModel surface.
