# QA Manual Edit Cross

Purpose: final QA policy and manual regression checklist for screen 4 manual edit, including click move, drag move, swap, Cross, undo, redo, save, load, and reset.

## Latest Manual Cross Policy

Manual Cross is a screen 4 editing feature that explicitly allows two assignment blocks to share the same grade time slot when the pair is intentionally linked.

Allowed only when all conditions are true:

- Same grade.
- Same RowSpan.
- Exactly two assignment endpoints.
- The source assignment is not already Cross-linked to another assignment.
- The target assignment is not already Cross-linked to another assignment.
- Same base `CourseId` with different sections is allowed when the assignment endpoints are distinct.

Blocked when any condition is true:

- Three or more assignments would be linked.
- RowSpan differs.
- Grade differs.
- Either endpoint is already part of another ManualCrossLink.
- The operation is a normal move, normal swap, or automatic validation path that is not explicitly creating or preserving a manual Cross link.

## Core Manual Edit Functions

- [ ] Click move: selecting a source and clicking a valid empty target moves the assignment.
- [ ] Click move: invalid target keeps the source assignment in place and shows a readable conflict reason.
- [ ] Drag move: dragging to a valid empty target moves the assignment.
- [ ] Drag move: dragging to an invalid target is blocked without corrupting selection state.
- [ ] Swap: valid source/target pair swaps assignments.
- [ ] Swap: physical range/third-block overlap is blocked; recordable constraint violations remain visible after the swap.
- [ ] Cross: hover or click Cross creation uses the assignment endpoints, not only `CourseId`.
- [ ] Cross: drag/drop Cross creation uses the same policy as click Cross creation.

## Cross Creation Scenarios

- [ ] Same grade, same RowSpan, different professor, different room, two endpoints only: Cross is allowed.
- [ ] Same base `CourseId`, different section, different professor, different room: Cross is allowed.
- [ ] Same professor or overlapping coteaching professor: Cross is allowed and the professor conflict remains visible.
- [ ] Same room: Cross is allowed and the room conflict remains visible.
- [ ] One-hour block with two-hour block: Cross is blocked.
- [ ] Already Cross-linked source to third target: Cross is blocked.
- [ ] Already Cross-linked target from third source: Cross is blocked.
- [ ] Different grade: Cross is blocked.

## SectionConflict and GradeConflict Policy

- [ ] Manual Cross may ignore `GradeConflict` only for the exact source-target assignment pair being Cross-linked.
- [ ] Manual Cross may ignore `SectionConflict` only for the exact source-target assignment pair being Cross-linked.
- [ ] General move records non-physical conflicts instead of rolling the edit back.
- [ ] Swap records non-physical conflicts instead of rolling the edit back.
- [ ] Automatic solver validation keeps existing hard constraints.
- [ ] Manual Cross does not suppress professor or room conflicts from the right-side list, block badge, or connector overlay.

## Save, Restore, Reset

- [ ] Save stores ManualCrossLink endpoint data for the exact assignment pair.
- [ ] Restore resolves the saved pair by assignment-level endpoint data: course, section, day, period, RowSpan, and room identity where applicable.
- [ ] Restore does not pick the first assignment with the same `CourseId` when sections differ.
- [ ] Reset restores the loaded baseline assignments and ManualCrossLink set.
- [ ] Reset does not clear saved Cross links that existed when the manual edit session was loaded.
- [ ] A new unsaved Cross can be undone after reset baseline is captured correctly.

## Undo and Redo

- [ ] Undo after move restores previous assignment positions.
- [ ] Undo after swap restores both assignments.
- [ ] Undo after Cross removes the new ManualCrossLink and restores UI sub-column state.
- [ ] Redo after Cross recreates the same ManualCrossLink and UI sub-column state.
- [ ] Undo/redo preserves saved/restored Cross links not involved in the current operation.

## UI Auxiliary Columns

- [ ] Cross-linked assignments render in separate sub-columns at the same grade/time slot.
- [ ] Cross labels match the internal ManualCrossLink pair.
- [ ] CrossParallelOrder is stable after save/load.
- [ ] CrossLinkLabels are assignment-level, not only `CourseId`-level.
- [ ] Hover Cross/Swap badges disappear when the pointer leaves or the target becomes invalid.
- [ ] UI auxiliary columns match internal ManualCrossLink state after move, swap, Cross, undo, redo, reset, save, and restore.

## Diagnostic Cleanup

- [ ] Professor diagnostic output is not required for normal QA.
- [ ] Manual Cross diagnostic output is not required for normal QA.
- [ ] Binding error diagnostics are tracked separately and removed or disabled before final demo.
- [ ] Temporary conflict filter logs are removed after policy verification.

## Manual Regression Scenario

1. Open a generated or saved timetable in screen 4.
2. Create a valid click move and verify the grid refreshes.
3. Undo and redo the click move.
4. Create a valid drag move and verify the grid refreshes.
5. Create a valid swap and verify both assignments move.
6. Create a valid Cross between two same-grade, same-RowSpan, different-professor, different-room assignments.
7. Create a valid same-base-CourseId Cross between distinct sections.
8. Create a professor-conflict Cross and verify the edit succeeds with a visible violation.
9. Create a room-conflict Cross and verify the edit succeeds with a visible violation.
10. Try different RowSpan Cross and verify it is blocked.
11. Try third-assignment Cross and verify it is blocked.
12. Save the timetable.
13. Leave manual edit and reopen the saved timetable.
14. Verify assignments, ManualCrossLink data, labels, and sub-columns are restored.
15. Reset and verify loaded Cross links remain.

## Regression Test Checklist

- [ ] Assignment-level manual Cross key tests.
- [ ] Same-base-CourseId manual Cross tests.
- [ ] ManualCrossLink save/load tests.
- [ ] CrossParallelOrder and CrossLinkLabels tests.
- [ ] SectionConflict and GradeConflict manual Cross exception tests.
- [ ] Move and swap conflict behavior tests.
- [ ] Undo, redo, and reset ManualCrossLink tests.
- [ ] WPF binding check for manual edit screen.
