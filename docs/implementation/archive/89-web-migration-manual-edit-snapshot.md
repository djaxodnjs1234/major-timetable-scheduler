# Web Migration Manual Edit Snapshot Document

1. Draft the web migration reference document for the manual edit feature.
   - Verify: the document states that manual edit uses an immutable generated timetable snapshot and does not react to later DB or constraint-setting changes.
2. Specify manual-edit data models, session lifecycle, editing operations, course/professor/team-teaching/room/multi-room changes, validation, save, and export behavior.
   - Verify: the document covers adding, updating, deleting courses and preserving assignment integrity.
3. Review the document for implementability and move this plan to `docs/implementation/archive/`.
   - Verify: search confirms the new migration document contains snapshot independence, constraint/manual separation, and course CRUD terms.
