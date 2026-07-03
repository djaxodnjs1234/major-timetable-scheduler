# Manual Edit Staging Drag Panel

## Goal
Make the manual edit block staging area behave like the timetable itself:
- fixed in the lower half of the right panel,
- rendered with the same block/card shape and grade color,
- supports drag from timetable to staging and from staging to timetable,
- selection/deselection follows the existing manual edit behavior.

## Assumptions
- A drag from a timetable block to the staging panel is equivalent to "stage selected block".
- A drag from a staged block to an empty timetable slot is equivalent to placing the selected staged block.
- Clicking the same staged block or empty staging space clears staged selection.
- Existing click-to-stage and click-to-place behavior should remain available.
- Staged blocks remain temporary editor state; save/export stays blocked while any staged block remains.

## Plan
1. Restructure the right panel layout.
   - Replace the single full-height right ScrollViewer with a two-row Grid.
   - Keep Inspector/Conflicts in the top half with its own ScrollViewer.
   - Pin the staging area to the bottom half.
   - Verify: WPF build succeeds and right panel content compiles.

2. Reuse the timetable block card rendering.
   - Expose the existing `UnifiedTimetableControl.MakeChipBorder` rendering helper for reuse.
   - Render staged items in code-behind so they use the same card shape, title/professor/room lines, and grade color.
   - Verify: staged cards are generated from `CellAssignment` and rebuild on collection/selection changes.

3. Add drag/drop between timetable and staging.
   - Add a public drag-data helper to `UnifiedTimetableControl` so external drop targets can recognize timetable drags.
   - On staging panel drop, stage the dragged timetable block through the ViewModel.
   - On staged card drag, start a drag payload that the timetable drop handler can place into an empty cell.
   - Verify: drag out/in uses the same ViewModel validation and undo snapshot path.

4. Align selection/deselection behavior.
   - Clicking an already selected staged block clears staged selection.
   - Clicking empty staging space clears staged selection.
   - Clicking an empty timetable cell with a staged block selected places it; clicking occupied timetable cells does not place.
   - Verify: add focused ViewModel tests for staged self-click/empty-space clear where practical, and run build/manual check.

5. Run verification and archive the plan.
   - Run WPF and ViewModel builds.
   - Run targeted tests if the existing test project compile blocker is resolved; otherwise record the blocker.
   - Run a small temporary console/manual check for staging behavior if xUnit remains blocked.
