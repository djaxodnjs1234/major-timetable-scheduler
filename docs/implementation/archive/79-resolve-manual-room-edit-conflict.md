# Resolve Manual Room Edit Conflict

## Goal

Resolve the manual edit ViewModel conflict after the teammate's room editing
changes, preserving both the new manual-room conflict filtering flow and the
graduate daytime manual move exception.

## Steps

1. Remove conflict markers in `ManualEditViewModel`.
   - Verify: no `<<<<<<<`, `=======`, or `>>>>>>>` remain.
2. Keep the teammate's `GetManualEditVisibleConflicts` path for save/display
   conflicts.
   - Verify: `ValidateBeforeSave` uses that helper.
3. Add the graduate daytime manual-edit exception into the shared visible
   conflict helper.
   - Verify: manual move/save conflict filtering is consistent.
4. Build the ViewModel project and document any unrelated test blockers.
   - Verify: changed project builds.
5. Archive this plan after verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
