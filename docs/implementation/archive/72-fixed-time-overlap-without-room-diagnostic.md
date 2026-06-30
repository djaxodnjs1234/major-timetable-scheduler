# Fixed Time Overlap Without Room Diagnostic

1. Reproduce the fixed-time overlap path when courses have no fixed room.
   - Verify: identify why the existing fixed-time overlap diagnostic misses this case and GE-025 appears later.
2. Add input diagnostics for fixed-time overlaps independent of room selection.
   - Verify: schedule generation reports a fixed-time overlap input error before solver generation errors.
3. Show the same save-blocking warning from course information management.
   - Verify: completing a course edit with overlapping fixed times keeps the edit open and shows the save warning popup path.
4. Update focused tests and run validation.
   - Verify: targeted ViewModel/diagnostic/WPF tests pass, solution builds, and this plan file is archived.
