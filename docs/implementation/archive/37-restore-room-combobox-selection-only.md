# Restore Room ComboBox Selection Only

1. Remove editable room ComboBox behavior from Manual Edit Inspector.
   - Verify: no room ComboBox uses `IsEditable=True` or text search binding.
2. Keep single-room change on string RoomId values.
   - Verify: single ComboBox uses `AvailableRoomIds` and `SelectedItem=NewRoomId`.
3. Simplify multi-room replacement candidates to string RoomId values.
   - Verify: multi replacement ComboBox uses `SelectedItem=SelectedReplacementRoomId`.
4. Add/update regression tests for string candidate lists and branch separation.
   - Verify: single and multi changes still pass without editable/search candidate objects.
5. Run requested build/test commands and archive this plan.
   - Verify: WPF build and full test suite pass.
