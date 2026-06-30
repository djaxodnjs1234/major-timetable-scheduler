# School Fixed Time Courses

1. Add persistent course fields for school-level fixed time courses.
   - Verify: database load/save, snapshots, and cloning preserve `IsSchoolFixed` and `SchoolFixedTargetGrade`.
2. Convert school fixed courses into solver blocking slots instead of scheduled assignments.
   - Verify: global blockers affect all non-fixed courses, grade blockers affect only the target grade, and user-fixed courses can override them.
3. Add course management controls and save-time warning behavior.
   - Verify: school fixed courses can omit professor data, require fixed slots, and ordinary courses overlapping a school blocker show a warning path instead of a hard save error.
4. Render school fixed courses in timetable views and Excel export as display-only blocks.
   - Verify: global blockers appear for all visible grades, target-grade blockers appear only for their grade, and saved timetable previews rebuild them from snapshot data.
5. Update focused tests and run validation.
   - Verify: solver, diagnostics, ViewModel, WPF source tests, export tests, and solution build pass; archive this plan file.
