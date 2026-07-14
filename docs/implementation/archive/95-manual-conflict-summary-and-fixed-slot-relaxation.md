# Manual Conflict Summary And Fixed Slot Relaxation

Goal: reduce manual-edit violation noise and allow fixed-slot input to ignore lunch and two-hour start-position restrictions.

Assumptions:
- Manual editing should still detect internal constraints, but the user-visible list and block badges should hide fixed-room, fixed-time, lunch, and two-hour block-start violations.
- Fixed-slot editing should allow any selected periods that match the course hours, including lunch periods and arbitrary consecutive pairs.
- Save/export validation should continue to use the same visible manual-edit conflict list unless existing stricter checks explicitly remain elsewhere.

Plan:
1. Audit manual conflict creation and display grouping.
   - Verify: identify which `ConflictType` values remain visible after filtering.
2. Filter user-visible manual conflicts.
   - Verify: fixed room, fixed time, lunch, and block-start conflicts are absent from `Conflicts` and block badges while professor/room/grade/section/unavailable conflicts remain.
3. Summarize the conflict panel by one compact code per violating block with expandable details.
   - Verify: each block-level item has a stable code and still exposes the existing detailed lines.
4. Relax fixed-slot editor block selection.
   - Verify: two-hour blocks can start at any period range in the picker and lunch periods are selectable according to ordinary period bounds.
5. Add focused tests and run the relevant WPF test subset.
   - Verify: new behavior is covered without broad unrelated rewrites.
