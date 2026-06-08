## Goal

Fix the WPF generated timetable results so professor name and course type do not disappear after auto-generation, while preserving or resolving course name, professor name, course type, room name, grade, section, day, and period without breaking existing bindings.

## Steps

1. Inspect the generated-results projection and render path.
   - Verify: Confirm the exact flow from solver assignments into `CellAssignment`, `TimetableGridViewModel`, `UnifiedTimetableViewModel`, and WPF result controls.

2. Add regression coverage for generated result display fields.
   - Verify: Tests fail before the fix when generated result chips do not expose the expected course type or resolved professor/room display text.

3. Implement the minimal metadata preservation or resolution fix.
   - Verify: Generated results keep ID-based core data and expose display-safe fields for course name, professor name, course type, and room name through the existing render models.

4. Validate generated results rendering end-to-end.
   - Verify: Targeted ViewModel/WPF tests pass and generated result bindings remain compatible.

5. Archive this plan after verification.
   - Verify: This file is moved to `docs/implementation/archive/` when the task is complete.
