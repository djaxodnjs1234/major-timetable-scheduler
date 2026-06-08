# WPF First Screen Excel Export All Grades

1. Locate first-screen Excel export path.
   - Verify: identify the command and exporter used by the first screen.

2. Preserve all grade data in export.
   - Verify: exported workbook contains timetable data for every grade present in the selected timetable.

3. Keep changes surgical.
   - Verify: only export-related code/tests are changed.

4. Add focused export coverage.
   - Verify: a test fails before the fix and passes after all-grade export is corrected.

5. Run targeted validation.
   - Verify: focused tests pass and WPF build errors are absent or unrelated.
