# Manual Conflict Display Text

Goal: adjust manual-edit constraint violation display text without changing the underlying conflict detection policy.

Assumptions:
- The right-side conflict panel should remain grouped by block.
- The right-side group header should not show an error code and should use `time / course name` ordering.
- Timetable block badges should show compact violation names again, not `ME-xx` codes.
- Conflict group ordering should be by weekday, then period.

Plan:
1. Update manual-edit block conflict titles and ordering.
   - Verify: group title is `day period / course` without grade or error code.
2. Remove error codes from the right-side conflict panel template and detail lines.
   - Verify: XAML no longer binds `DisplayCode` in the panel header.
3. Restore timetable in-grid badges to compact violation labels.
   - Verify: grid labels contain values such as `교수 중복` or `강의실 중복`.
4. Update focused tests and run targeted verification.
   - Verify: manual freedom/lunch/time-picker focused tests pass, plus WPF build.
