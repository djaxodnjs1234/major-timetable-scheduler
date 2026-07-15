# Final Validation Retake and Course Room Checks

## Goal

Add two final validation checks to Manual Edit's full validation dialog:

- Retake consideration: retake-safe course placements should remain valid.
- Same room per course: each course group should use one consistent room across its placed blocks/sections unless fixed multi-room usage explicitly requires multiple simultaneous rooms.

## Assumptions

- These checks are final validation checks only; they should not add timetable badge text or right-side live violation items unless later requested.
- Existing hidden/manual-edit freedom rules remain unchanged.
- Existing solver constraints remain the source of truth for generated schedules; this change verifies manual edits before save/export.

## Plan

1. Inspect current retake and course-room consistency constraints/tests.
   - Verify: identify the equivalent solver constraint semantics and current validation pipeline.
2. Add conflict types and final-validation-only detection for retake and course room consistency.
   - Verify: full validation check list includes the two new rows and details use the existing location/block-name detail layout.
3. Keep the new checks out of timetable badges and right-side live violations.
   - Verify: manual visible conflict filtering does not include the new final-only types.
4. Add focused regression tests.
   - Verify: failing cases are shown in full validation and normal cases pass.
5. Run focused tests and WPF build.
   - Verify: tests and build pass.
6. Archive this plan after verification.
   - Verify: the plan file is moved to `docs/implementation/archive/`.
