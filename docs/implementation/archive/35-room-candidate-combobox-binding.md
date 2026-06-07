# Room Candidate ComboBox Binding

1. Confirm the current candidate collection and ComboBox binding types.
   - Verify: identify whether candidates are strings or displayable room objects.
2. Introduce a small room candidate view model for display and selected RoomId binding.
   - Verify: ComboBox `ItemsSource` uses displayable objects while `SelectedValue` remains a string RoomId.
3. Split and harden single-room and replacement candidate builders.
   - Verify: single-room candidates fall back to session rooms; replacement candidates exclude assigned rooms and fall back when filtered empty.
4. Refresh property notifications on selection, old-room changes, success, and reset paths.
   - Verify: candidates update immediately when `SelectedOldRoomId` changes.
5. Run requested build/test commands and archive this plan.
   - Verify: WPF build and full test suite pass.
