# School Fixed Field Locking and Display Text

1. Lock and clear non-applicable course fields when school fixed is enabled.
   - Verify: grade, section professor, coteach professors, unavailable rooms, and fixed rooms are cleared/disabled in the course editor.
2. Render school fixed blocks with only the required display title.
   - Verify: timetable cells show `[학교고정] Name` for all-grade blockers and `[학년고정] Name` for grade-specific blockers, with no professor/room extra lines.
3. Export school fixed blocks with the same title-only text.
   - Verify: Excel visible timetable text contains the bracketed title and no room/professor text for school fixed display rows.
4. Run focused build/tests and archive this plan.
   - Verify: relevant ViewModel/export tests pass and WPF project builds.
