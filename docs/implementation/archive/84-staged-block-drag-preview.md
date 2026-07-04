# Staged Block Drag Preview

## Goal

Make blocks dragged from the manual edit staging panel show the same mouse-following block preview used by normal timetable drag moves.

## Plan

1. Inspect existing timetable drag preview behavior.
   - Verify: identify the existing preview adorner and how it is created, updated, and removed.
2. Reuse the existing preview adorner from the staging panel.
   - Verify: avoid duplicating card rendering or preview drawing logic.
3. Wire preview lifecycle into staged block drag start, drag movement, and drag completion.
   - Verify: staged blocks follow the cursor while dragging and the preview is removed after drag ends.
4. Build and run available checks.
   - Verify: ViewModel and WPF projects build; test project status is reported if blocked by existing compile errors.
