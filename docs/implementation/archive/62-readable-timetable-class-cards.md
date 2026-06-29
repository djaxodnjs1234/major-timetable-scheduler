# Readable timetable class cards

Assumption: the requested typography change applies to WPF timetable class blocks in all timetable views because the standard grid reuses `UnifiedTimetableControl.MakeChipBorder`.

1. Inspect the existing class-card text layout in `UnifiedTimetableControl`.
   - Verify: identify title, professor, and room `TextBlock` settings and shared usage from `TimetableGridControl`.
2. Update the class-card text layout.
   - Verify: course title is larger, professor/room text stays smaller, metadata uses separate lines, and long text is constrained with ellipsis.
3. Add focused source-level regression coverage.
   - Verify: tests check the title/metadata sizing and ellipsis settings without relying on WPF visual initialization.
4. Run focused WPF tests and build.
   - Verify: report the exact commands and any environment-limited failures.
