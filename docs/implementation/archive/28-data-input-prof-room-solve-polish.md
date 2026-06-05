1. Update DataInput ViewModel lists and IDs.
   - Verify: course/professor/room lists sort by numeric ID, and professor/room add commands auto-generate numeric IDs from name-only input.
2. Adjust professor details.
   - Verify: ID is hidden in details, editing is disabled until Modify, unavailable slots show period plus time, selected unavailable slots render gray with X, and room restrictions are labeled/displayed as unavailable rooms by room name plus lab/capacity.
3. Adjust room details.
   - Verify: ID is hidden in details, editing is disabled until Modify, lab checkbox and capacity input save, list rows include lab/capacity, and xlsx-imported rows use the light gray imported style.
4. Polish course and Cross UI.
   - Verify: course list columns align after removing the Cross column, courses sort by ID, Cross tooltip explains the term, Cross add candidates hide ID/type, group by grade, and exactly two selected courses are required.
5. Persist room metadata and Cross settings.
   - Verify: room lab/capacity roundtrip through SQLite and CrossGroups continue to roundtrip through workspace DB/saved snapshots.
6. Polish solver wording/tooltips.
   - Verify: user-facing labels hide SC/HC jargon, solver parameter names are clearer, and tooltips explain soft constraints, candidate count, time limit, and each constraint.
7. Run tests, build, and manual surface QA.
   - Verify: focused ViewModel/data tests, full test suite, isolated WPF build, and a small driver for add/save/Cross behavior pass.
