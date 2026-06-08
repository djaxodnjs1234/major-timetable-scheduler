# WPF Exclude Single-Section Courses From Auto Solve

1. Locate auto-generation input path.
   - Verify: identify the ViewModel method that builds the solver input for timetable generation.

2. Filter single-section course groups.
   - Verify: only course base IDs with two or more sections are passed to the automatic solver.

3. Preserve management data.
   - Verify: single-section courses remain visible/editable in course management and are only excluded from solving.

4. Add focused tests.
   - Verify: solve input excludes single-section courses and keeps multi-section courses.

5. Run focused validation.
   - Verify: related tests and WPF build pass or unrelated failures are documented.
