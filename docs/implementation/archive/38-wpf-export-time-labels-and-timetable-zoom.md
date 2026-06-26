# WPF export time labels and timetable zoom

1. Add time ranges to the visible period labels in formatted Excel exports.
   - Verify: exported timetable sheets show `1교시` with `09:00~10:00`, while the hidden data sheet keeps numeric periods for round-trip import.
2. Add zoom controls to every WPF timetable display (results, saved timetable preview, and manual edit).
   - Verify: minus, reset, and plus controls resize every tab's timetable within safe minimum and maximum zoom levels.
3. Add focused regression tests and run the WPF solution test suite.
   - Verify: export labels and zoom bounds pass; the full suite is run to identify unrelated environment failures.
