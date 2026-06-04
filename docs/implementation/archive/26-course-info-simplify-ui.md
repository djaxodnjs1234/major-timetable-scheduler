1. Remove fixed-classroom editing from the course information UI.
   - Verify: no `GroupFixedRoomsPicker` or `고정 강의실` course-management control remains in `DataInputView`.
2. Compact the basic course information editor.
   - Verify: basic fields use smaller controls and the course name is displayed read-only instead of as an editable text box.
3. Generate course IDs automatically.
   - Verify: manual course add does not require an ID and creates course IDs starting at `1`; XLSX import renumbers course base IDs from `1` while preserving section counts.
4. Align the course list columns and show section counts.
   - Verify: course header and row grids use shared column sizing, and the section column shows counts like `2개`.
5. Clean up matching code-behind bindings.
   - Verify: no code-behind lookup remains for the removed fixed-classroom picker.
6. Run focused checks.
   - Verify: C# build/tests pass with isolated output, and UI driver/source checks confirm the intended surface.
