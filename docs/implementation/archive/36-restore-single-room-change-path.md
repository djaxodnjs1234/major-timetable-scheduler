# Restore Single Room Change Path

1. Restore the single-room room-change candidate and ComboBox binding to string RoomId values.
   - Verify: single-room ComboBox uses `AvailableRoomIds` and `SelectedItem=NewRoomId`.
2. Split `ApplyRoomChange` into single-room and multi-room branches.
   - Verify: single-room branch only uses `NewRoomId`; multi-room branch uses old/replacement room selections.
3. Keep multi-room partial replacement behavior unchanged.
   - Verify: only the selected old room changes and other assigned rooms remain.
4. Add focused regression tests.
   - Verify: single branch does not require multi-room state and multi branch ignores `NewRoomId`.
5. Run requested build/test commands and archive this plan.
   - Verify: WPF build and full test suite pass.
