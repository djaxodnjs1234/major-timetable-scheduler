# Manual edit staging density and inspector selection

## Assumptions

- The "block storage" means the staging panel in the manual edit screen.
- Clicking a staged block should select it for the inspector without placing it on the timetable.
- A staged block is edited with the same inspector controls where possible, but it should remain in the staging panel until the user drags it.

## Plan

1. Inspect the current staging panel item template and inspector selection flow.
   - Verify: identify where staged blocks are rendered and how selected timetable blocks are assigned.
2. Reduce staging panel text size and vertical padding, and remove the empty-space drag hint.
   - Verify: XAML builds and staging items use smaller, more compact layout.
3. Allow clicking a staged block to populate the inspector.
   - Verify: staged item click sets the selected assignment/course used by the inspector.
4. Show "없음" when the selected course has no team-teaching professors.
   - Verify: inspector displays a clear empty state instead of a blank area.
5. Limit relevant dropdown popups to 8 visible rows with scrolling.
   - Verify: professor/team-teaching/room dropdowns retain selection behavior and expose scrollable popup limits.
6. Run focused manual-edit tests and WPF build.
   - Verify: existing relevant tests pass and the WPF project builds.
