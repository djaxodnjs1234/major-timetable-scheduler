# Timetable Selection Compact List

## Goal

Make the WPF timetable selection screen easier to scan when saved timetable names are long.

## Assumptions

- The request applies only to the WPF timetable selection screen.
- Saved timetable names should stay on one line, show an ellipsis when clipped, and expose the full name in a tooltip.
- The list should become denser by reducing item padding/margins and text spacing without changing behavior.

## Steps

1. Inspect the timetable selection XAML.
   - Verify: identify the saved timetable list item template and existing sizing.
2. Update the saved timetable list item template.
   - Verify: the name TextBlock uses trimming, no wrapping, and a full-name tooltip.
   - Verify: item padding/margins are reduced and the content can shrink inside the list width.
3. Make the timetable preview on the selection screen denser without changing default grid sizing elsewhere.
   - Verify: compact sizing is opt-in on the selection view controls.
4. Add a focused WPF test if existing test structure supports source-level XAML checks.
   - Verify: tests assert trimming, tooltip binding, and compact sizing markers.
5. Run targeted tests and build the WPF project.
   - Verify: the focused test passes and WPF build succeeds.
6. Archive this plan after verification.
   - Verify: this file is moved to docs/implementation/archive/.
