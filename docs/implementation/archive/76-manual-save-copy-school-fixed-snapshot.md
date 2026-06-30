# Manual Save Copy Preserves School Fixed Snapshot

1. Trace manual-edit save-copy flow and reproduce the empty saved timetable case.
   - Verify: a saved copy made from a school-fixed-only or school-fixed-visible timetable keeps enough data to preview from the timetable list.
2. Fix the save-copy path so display-only school fixed courses are persisted through the snapshot, not lost with empty real assignments.
   - Verify: timetable selection preview reconstructs `[학교고정]` / `[학년고정]` blocks after saving a copy.
3. Run focused tests and archive this plan.
   - Verify: relevant ViewModel tests pass and WPF project builds if touched.
