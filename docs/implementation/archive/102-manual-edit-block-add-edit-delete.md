# Manual Edit Block Add Edit Delete

## Goal

Add block-level add, edit, and delete operations to the manual edit screen. Newly added blocks should appear in the block staging area and be managed as whole blocks. Special school-fixed and grade-fixed blocks must also be addable and movable.

## Steps

1. Inspect the existing staged block model, drag/drop placement, and school-fixed display handling.
   - Verify: identify the minimum fields needed to create staged blocks without breaking placement.

2. Add ViewModel commands and state for creating, editing, and deleting staged blocks.
   - Verify: added blocks appear in `StagedBlocks`, can be selected, and can be removed.

3. Support special fixed blocks in staged block creation and placement.
   - Verify: school-fixed/grade-fixed style blocks can be represented as staged block items and placed on the timetable.

4. Add WPF controls for block add/edit/delete in the staging area.
   - Verify: commands are reachable from the manual edit screen.

5. Add focused tests and run build/tests.
   - Verify: staged block add/edit/delete behavior is covered and WPF compiles.
