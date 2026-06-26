# Refine timetable zoom and investigate graduate capacity

1. Move the zoom controls into the unified timetable tab header in each affected timetable view.
   - Verify: the controls are shown beside the "통합 시간표" label and still operate all timetable views.
2. Inspect GE-25 and reproduce it against the local `2026-2 v2.0` saved timetable database when available.
   - Verify: document the exact diagnostic condition and affected data.
3. Check the diagnostic coverage for six three-hour graduate courses and add missing pre-solve validation if needed.
   - Verify: a capacity issue is reported before solver execution when the input is impossible.
4. Run focused tests and build, then archive this plan.
   - Verify: affected test suites and WPF build pass.
