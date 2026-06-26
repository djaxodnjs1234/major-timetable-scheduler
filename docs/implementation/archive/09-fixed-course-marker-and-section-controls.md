# Fixed Course Marker and Section Controls

## Goal
Show fixed-time courses with a star in course management and timetable views, and allow section count adjustment even when a course is fixed. Adjusting sections must clear fixed-time settings automatically.

## Assumptions
- Timetable cell labels already carry `CellAssignment.IsFixed`; this path should keep using the existing star marker.
- The course management list header is a grid of fields, so it needs an explicit fixed marker column instead of relying only on `DisplayLabel`.
- Section count changes should clear `IsFixed` and `FixedSlots` from existing sections before adding or removing sections.

## Steps
1. Add a fixed marker property to `CourseGroupItem` and bind it in the course management list header.
   - Verify: fixed course groups expose `★`, non-fixed groups expose an empty marker.
2. Make section controls visible for fixed individual courses.
   - Verify: the UI no longer hides the add/remove section controls just because the course is fixed.
3. Clear fixed-time settings when adding or removing sections.
   - Verify: existing persisted sections have `IsFixed=false` and empty `FixedSlots` after section count changes.
4. Add or update regression tests.
   - Verify: tests cover course list star marker, timetable fixed marker, and section adjustment reset behavior.
5. Run targeted tests and archive this plan.
   - Verify: relevant ViewModel tests pass, with known unrelated failures noted if a broader suite is run.
