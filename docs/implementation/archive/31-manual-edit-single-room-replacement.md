# Manual Edit Single Room Replacement

1. Confirm the current multi-room representation and existing room-change command.
   - Verify: multi-room lessons are represented by multiple `SolutionAssignment` rows and aggregated into `CellAssignment.Rooms`.
2. Add ViewModel state for selecting the assigned room to replace and the replacement room.
   - Verify: assigned-room options come only from the selected cell, while replacement options exclude already assigned rooms.
3. Extend the existing room-change command to replace only the selected old room.
   - Verify: other rooms in the selected lesson remain unchanged, conflicts reject before mutating `_working`, and undo snapshots are only created on success.
4. Update the Inspector UI without removing the existing single-room path.
   - Verify: single-room selections keep the existing room-change UI; multi-room selections show old-room and new-room ComboBoxes.
5. Add focused tests, run the requested build and test commands, and archive this plan.
   - Verify: build and full test suite pass.
