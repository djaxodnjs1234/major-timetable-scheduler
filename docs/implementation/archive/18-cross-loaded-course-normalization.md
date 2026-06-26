# Cross Loaded Course Normalization

## Goal
Fix the WPF-only case where a valid Cross group can still fall through to the generic `GE-025` failure until the user opens a course editor and saves without changes.

## Assumptions
- The user scenario points to a mismatch between loaded course data and course data normalized by the edit/save flow.
- The fix should keep Cross validation specific and should avoid widening unrelated solver behavior.
- The WPF surface is the only target.

## Steps
1. Trace course load, edit/save, Cross add, generation diagnostics, and solver snapshot paths.
   - Verify: identify which course field changes after a no-op save.
2. Add normalization at the earliest shared WPF data boundary needed for generated schedules.
   - Verify: valid same-grade/same-hours/same-block Cross data behaves the same before and after a no-op course save.
3. Add regression coverage for the loaded-data case.
   - Verify: tests reproduce the pre-save issue and pass after the fix.
4. Run targeted tests and WPF build.
   - Verify: relevant tests pass and the WPF project builds.
5. Archive this plan after verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
