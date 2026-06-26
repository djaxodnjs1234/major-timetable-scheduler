# Preserve course professors when weekly hours change

1. Reproduce changing weekly hours for an edited course with an assigned professor.
   - Verify: the professor selector and course edit model retain the selected professor after the hours-change event.
2. Correct the course-hour update path so it does not clear or replace professor assignments.
   - Verify: saving and rebuilding the course list keeps the professor ID.
3. Run focused WPF and ViewModel tests.
   - Verify: the regression test and related existing tests pass.
