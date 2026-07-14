# Manual Edit Simple Block Add and Inspector Edit

## Goal

Simplify manual edit block creation and make the inspector the main place for editing and deleting a selected timetable block.

## Assumptions

- "Add" creates a new staged manual block using course name, primary professor name, and room name/text, then places it in the block storage area.
- The staged add form should appear above the block storage list.
- Inspector edits apply to the selected timetable block, not to every course with the same course id unless the selected block represents that course occurrence.
- Primary professor and room must each keep at least one value.
- Team-teaching professors and rooms can be added one at a time from dropdowns and removed one at a time.
- Deleting from the inspector removes only the selected rendered block from the manual timetable.

## Steps

1. Inspect existing manual block staging, inspector room editing, and course/professor data flow.
   - Verify: identify the smallest ViewModel/XAML surface to change.
2. Replace the staged block add form with simple text inputs for course name, professor name, and room.
   - Verify: creating a block adds one item to `StagedBlocks` with a session-local course/professor/room.
3. Extend inspector editing with primary professor, team-teaching professor, and room add/remove operations.
   - Verify: primary professor and rooms cannot become empty; added items update rendered cells and conflict detection.
4. Add inspector delete for the selected block only and remove unnecessary inspector sections.
   - Verify: delete removes the selected block rows and clears selection without deleting other blocks.
5. Update focused ViewModel tests and run targeted tests plus WPF build.
   - Verify: tests pass and WPF project builds.

