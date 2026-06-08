## Goal

Fix timetable block display consistency across timetable selection, solution preview, and manual edit screens, and preserve professor/type metadata when a saved timetable is reopened for editing.

## Plan

1. Trace the display and edit pipeline for timetable blocks and saved timetable course metadata.
   - Verify: identify the exact functions/files used by selection, preview, manual edit, save, reopen, and detail panels.

2. Patch the shared block text formatting to follow one rule everywhere.
   - Verify: rendered block text includes fixed marker, course name, section, professor names including coteachers, and room names including multi-room.

3. Patch the saved timetable reopen flow so course detail metadata is preserved.
   - Verify: reopening a saved timetable for editing keeps professor name(s) and course type in course detail management.

4. Run diagnostics and exercise the save/reopen/edit workflow through the app-facing surface.
   - Verify: changed files are clean, and an end-to-end save -> selection edit workflow reproduces the fixed behavior.
