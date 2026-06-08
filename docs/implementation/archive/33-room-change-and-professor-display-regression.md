# Room Change and Professor Display Regression

1. Inspect the single-room room-change UI conditions and command branch.
   - Verify: identify why single-room selections can lose the existing room-change ComboBox.
2. Restore the single-room change path without changing multi-room replacement.
   - Verify: single-room selections show current room, replacement ComboBox, and apply through `NewRoomId`; multi-room selections still require old-room selection.
3. Deduplicate professor display names only in display helpers.
   - Verify: primary professor appears first, duplicated coteaching professor IDs are shown once, and professor view filters stay unchanged.
4. Add focused regression tests.
   - Verify: single-room UI/change works, multi-room replacement still works, display deduplicates and preserves order.
5. Run requested build/test commands and archive this plan.
   - Verify: WPF build and full test suite pass.
