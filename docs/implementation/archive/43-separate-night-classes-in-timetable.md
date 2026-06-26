# Separate night classes in the timetable

1. Extend timetable grid layout metadata to expose a night-class separator before period 10.
   - Verify: daytime and night periods render with an explicit separator row while preserving slot mapping.
2. Apply the separator consistently to unified and standard timetable controls.
   - Verify: selection, results, and manual-edit views show the same night-class boundary.
3. Add focused layout tests and run WPF build/tests, then archive this plan.
   - Verify: period ordering, separator placement, and existing lunch layout remain correct.
