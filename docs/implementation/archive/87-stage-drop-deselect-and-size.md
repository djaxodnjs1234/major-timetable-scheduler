# Stage Drop Deselect And Size

## Goal

When a timetable block is moved into the staged block cabinet, the block should not remain selected. Also reduce the staged block cabinet height to two-thirds of its previous half-panel size.

## Plan

1. Stop auto-selecting newly staged blocks.
   - Verify: staging a selected timetable block leaves both timetable and staged selections empty.
2. Update the staging success message to match the deselected state.
   - Verify: message no longer tells the user to click an empty timetable cell immediately.
3. Reduce the staged cabinet layout height.
   - Verify: the right panel uses a 2:1 top-to-cabinet row ratio.
4. Build and run available checks.
   - Verify: ViewModel and WPF code compile; report any existing test blockers.
