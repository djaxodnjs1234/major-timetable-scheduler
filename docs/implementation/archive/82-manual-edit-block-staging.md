# Manual Edit Block Staging Panel

## Goal
Allow users to temporarily remove a visual timetable block from the manual edit grid into a lower-right side panel, then place that block back into a valid empty slot.

## Assumptions
- "Remove a block" means staging one visual occurrence temporarily, not deleting the course from the timetable permanently.
- A staged block must keep its course identity, assignment id, duration, room rows, and multi-room rows intact.
- Saved timetable records should remain complete. Save/export should be blocked while any staged block is still outside the grid.
- This feature is additive to the existing click/drag move workflow.

## Risks Found
- Visual blocks are not first-class persisted objects. They are reconstructed from multiple `SolutionAssignment` rows grouped by assignment id, day, period range, and room set.
- Removing only one row of a multi-period or multi-room block would corrupt the rendered block and validation state.
- The current save/export path writes only `_working`; staged rows would be lost unless save/export is blocked or the database schema changes.
- The current conflict detector does not treat missing course hours as a normal conflict, so staged blocks need explicit validation.
- Manual cross links reference assignment identities and slots. Removing a linked block can leave stale cross links unless they are removed or restored through undo/redo.
- Existing move validation assumes the source block is still in `_working`; placing a staged block needs a reusable placement validator that does not require source rows.
- The right panel is already dense, so the staging UI needs compact fixed-height scrolling.

## Plan
1. Add a small staging model in `ManualEditViewModel`.
   - Introduce a `StagedBlockItem` record/view model with display text and original assignment rows.
   - Add an observable staged-block collection, selected staged block, and properties for empty/non-empty panel states.
   - Verify: unit test can load a timetable and see an empty staged collection.

2. Add commands for removing the selected grid block.
   - Reuse `TryGetMovingAssignments` to capture the complete visual block rows.
   - Remove all rows for that block from `_working`.
   - Remove invalid/manual cross links involving that block.
   - Push an undo snapshot, clear selection, rerender, and refresh command states.
   - Verify: removing a two-hour or multi-room block removes all rows and leaves no partial rendered cell.

3. Add placement flow for staged blocks.
   - Add a command or click target state that lets the selected staged block be inserted into an empty grid slot.
   - Split the existing move candidate/validation logic so staged placement can validate grade, period range, lunch, time band, overlap, and conflict warnings without requiring a source row.
   - Preserve original room ids and assignment id when creating rows at the target slot.
   - Verify: placing a staged block restores row count, renders the block at the target slot, and respects blocked/warning cases.

4. Make undo/redo and reset include staged blocks.
   - Add staged blocks and selected staged block id to `ManualEditSnapshot`.
   - Update snapshot equality so `HasUnsavedChanges` changes when blocks are staged or restored.
   - Verify: undo after remove restores the grid; redo returns the block to the panel; reset returns to baseline.

5. Guard save/export and validation.
   - Update `ValidateBeforeSave` / `ValidateBeforeExport` to block while staged blocks remain.
   - Keep the saved timetable schema unchanged.
   - Verify: save/export with a staged block is blocked and does not write a partial timetable; after placing it back, save works.

6. Add the lower-right panel UI.
   - Add a compact `Expander` in `ManualEditView.xaml` under the existing inspector/conflicts area.
   - Include selected-block "remove" action, staged block list, and "place selected" state text.
   - Keep the list scroll-bounded so the 360px right panel remains usable.
   - Verify: run the WPF app, confirm the panel appears and the existing inspector/conflict panel still fits.

7. Run focused verification.
   - Run the relevant `ManualEditViewModelTests` filter.
   - Run the WPF project build.
   - Note: the broader test project currently has an unrelated compile failure in `TimetableSelectionViewModelTests` referencing `ExportXlsxCommand`; focused verification may need that pre-existing issue fixed first.
