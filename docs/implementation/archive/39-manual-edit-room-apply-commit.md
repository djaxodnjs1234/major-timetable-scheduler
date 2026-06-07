1. Guard zero room-change matches -> verify: no commit, rerender success, or undo snapshot happens when no working assignment is changed.
2. Preserve single and multi room change paths -> verify: single uses NewRoomId and multi uses SelectedReplacementRoomId only.
3. Add save/load persistence tests -> verify: changed RoomId survives saved timetable roundtrip.
4. Run build and tests -> verify: WPF project builds and test project passes.
