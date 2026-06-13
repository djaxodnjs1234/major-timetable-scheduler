# QA Final Checklist

Purpose: final verification checklist before submission, maintenance handoff, or professor demo.

## App-Wide Checks

- [ ] App starts without build, runtime, or WPF binding errors.
- [ ] Main navigation between screens 1 to 4 works without stale state.
- [ ] Existing saved timetables can be opened, previewed, edited, and saved again.
- [ ] Korean labels, course names, professor names, room names, and timetable names render correctly.
- [ ] No temporary diagnostic output is required for normal use.
- [ ] Known unresolved issues are recorded in the section at the bottom of this file.

## Screen 1: Timetable Selection

- [ ] Saved timetable list loads correctly.
- [ ] Selecting a saved timetable updates the preview.
- [ ] Preview shows course title, professor, room, grade, section, and time clearly.
- [ ] All-grade Excel export includes every grade column expected for the saved timetable.
- [ ] Opening a saved timetable for manual edit preserves its saved snapshot data.
- [ ] Empty or missing saved timetable states show readable empty text instead of broken layout.

## Screen 2: Information Input

- [ ] Course groups show section rows without losing per-section data.
- [ ] Course add, edit, delete, fixed slot, room, professor, and coteaching data remain editable.
- [ ] Professor and room metadata roundtrip through save/load.
- [ ] Fixed room and fixed time settings remain distinct: fixed time controls time, fixed room controls room.
- [ ] Course operating information remains readable and does not overlap adjacent controls.
- [ ] Solver input excludes single-section courses from automatic solving while keeping them visible/editable.

## Screen 3: Solution Preview

- [ ] Generated solutions appear with readable timetable cards.
- [ ] Course title, professor, room, grade, and section are visible where expected.
- [ ] Preview density is readable at normal screen size.
- [ ] Moving back to information input keeps the current session state intact.
- [ ] Selecting a solution and moving to manual edit preserves professor, room, course, and Cross metadata.

## Screen 4: Manual Edit

- [ ] Click move works for valid destinations and blocks invalid destinations.
- [ ] Drag move works for valid destinations and blocks invalid destinations.
- [ ] Swap works only when both resulting assignments satisfy normal constraints.
- [ ] Manual Cross works only under the latest policy documented in `QA_MANUAL_EDIT_CROSS.md`.
- [ ] Undo restores the previous assignment and ManualCrossLink state.
- [ ] Redo reapplies the assignment and ManualCrossLink state.
- [ ] Reset restores the loaded baseline assignments and ManualCrossLink state.
- [ ] Saving and reopening preserves assignments, rooms, professors, Cross links, labels, and sub-column order.
- [ ] Conflict messages are readable and use final user-facing titles.

## Save, Load, Reset

- [ ] Saving a manually edited timetable creates or updates the intended saved timetable.
- [ ] Loading a saved timetable uses the saved snapshot, not the current unrelated workspace state.
- [ ] Reset returns to the loaded manual edit baseline, including manual Cross links.
- [ ] Save/load does not convert manual Cross into normal solver Cross groups.
- [ ] Timetable names display consistently in list, preview, and manual edit flows.

## Excel Import and Export

- [ ] Excel import expands section data consistently.
- [ ] Imported course IDs, titles, professors, rooms, hours, and fixed settings are readable in the UI.
- [ ] Auto-incremented course IDs do not collide with existing course IDs.
- [ ] Excel export from screen 1 includes all expected grade columns.
- [ ] Excel export remains a user-facing output format; saved timetable state is restored from internal snapshot data.

## UI and Readability

- [ ] Timetable cards have readable title, professor, room, and section text.
- [ ] Card corners, borders, and colors are visually consistent.
- [ ] Grade color legend is understandable.
- [ ] Tooltips are readable and not overly dense.
- [ ] Hover badges for Cross/Swap do not remain stuck after mouse movement.
- [ ] Empty detail/preview areas show intentional empty states.

## Professor Demo Must-Pass

- [ ] Start app and navigate through screens 1 to 4 without errors.
- [ ] Import or use prepared data, generate a solution, preview it, and enter manual edit.
- [ ] Perform one valid move, one valid swap, one valid Cross, one blocked invalid Cross, undo, redo, reset, save, reopen.
- [ ] Export the final timetable to Excel.
- [ ] Confirm no diagnostic logs or trace-only controls are needed during the demo.

## Unresolved Issues

- [ ] Check whether `RoomChange_WorkspaceOnlyRoom_SaveLoadRestoresRoomId` is still failing or already resolved.
- [ ] Check whether duplicate `Course.Id` metadata in unified render is still a limitation for same-CourseId scenarios.
- [ ] Record any active WPF binding errors separately before release.
