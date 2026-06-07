1. Add course-level unavailable room data without removing legacy fixed-room support.
   - Verify: course snapshots and SQLite roundtrip include `UnavailableRooms`; solver excludes those rooms before professor room candidates.
2. Improve fixed-course slot editing.
   - Verify: dropdowns show time ranges, not raw start periods; 2-hour blocks only offer 1~2, 3~4, 6~7, 8~9.
3. Add Data Input course-row summaries and edit state.
   - Verify: key fields are previewed on the row, long values trim, and the gray bordered basic/operation fields are disabled until edit mode.
4. Refactor course management labels and controls.
   - Verify: professor, grade, weekly hours, and block structure use dropdowns; department is removed; requested Korean labels/tooltips are present.
5. Expose Cross pair settings for same-grade/same-block courses.
   - Verify: toggling a candidate creates/removes a persisted pair-wise `CrossGroup`.
6. Run tests, build, and a minimal ViewModel/UI-surface check.
   - Verify: automated checks pass and the edited screen behavior works through ViewModel commands/bindings where headless QA is possible.
