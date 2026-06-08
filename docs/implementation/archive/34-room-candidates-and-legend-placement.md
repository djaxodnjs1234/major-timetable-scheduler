# Room Candidates and Legend Placement

1. Split single-room and multi-room candidate lists.
   - Verify: single-room ComboBox binds to its own candidate list; multi-room replacement excludes already assigned rooms with fallback.
2. Refresh candidate notifications on selection, old-room changes, success, and reset paths.
   - Verify: `ApplyRoomChangeCommand` can execute for valid single-room and multi-room changes.
3. Move grade legend out of timetable controls.
   - Verify: timetable controls no longer overlay the legend inside the grid.
4. Add a shared grade legend control and place it in timetable view/tab headers.
   - Verify: all unified and tabbed timetable areas show the legend above/outside the grid and reuse `GradeToBrush`.
5. Run requested build/test commands and archive this plan.
   - Verify: WPF build and full test suite pass.
