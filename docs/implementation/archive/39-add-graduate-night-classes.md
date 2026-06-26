# Add graduate-only night classes

Assumption: night classes use periods 10-13 (`18:00~22:00`), graduate courses use only night periods, and undergraduate courses cannot use them.

1. Define shared daytime and night period sets, then extend solver variables and block rules through period 13.
   - Verify: a two-hour night block can start only at periods 10 or 12 and cannot cross lunch or the end of the night band.
2. Add a graduate-only time-band hard constraint and input diagnostics for incompatible fixed slots.
   - Verify: every undergraduate assignment is daytime, every graduate assignment is night, and invalid fixed times report a clear diagnostic.
3. Extend editors, grids, Excel import/export, and capacity diagnostics for night periods.
   - Verify: 18:00~22:00 appears in every timetable and time picker, exports retain night assignments, and grade capacity uses its permitted time band.
4. Add solver, diagnostics, UI-view-model, and Excel regression tests.
   - Verify: targeted tests and WPF build pass; the full suite is run to identify unrelated environment failures.
