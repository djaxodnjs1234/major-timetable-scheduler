## Goal
- Fix workbook import defaults for professor/type/block structure.
- Prevent single-section capstone courses from showing a bogus F section.
- Prevent fixed-time save in course details from splitting a single course into multiple sections.
- Fix saved timetable preview on the selection screen.

## Plan
1. Inspect xlsx import rules in `wpf/TimetableScheduler.Data/XlsxLoader.cs` and compare them with current display/save behavior.
   - Verify: identify the exact defaulting logic and capstone section derivation path.
2. Patch xlsx import defaults and capstone single-section handling in the smallest affected loader code.
   - Verify: imported 3-hour courses default to `1+2`, 4-hour to `2+2`, professor/type remain populated, and single capstone imports as one section.
3. Patch course-detail fixed-time save behavior so a single course remains a single course.
   - Verify: turning on fixed time and saving does not convert one course into multiple section rows.
4. Patch saved timetable preview rendering to use the saved snapshot when available.
   - Verify: a saved timetable can still render even if the live workspace no longer matches its saved course/room/professor set.
5. Add regression tests for the loader, fixed-time save path, and saved-preview behavior.
   - Verify: targeted xUnit tests fail before the fix and pass after it.
6. Run targeted verification and a minimal surface-level manual QA flow.
   - Verify: `dotnet test` passes for touched areas and the preview path works through its viewmodel surface.
