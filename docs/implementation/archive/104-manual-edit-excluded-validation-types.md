# Manual Edit Excluded Validation Types

## Goal

In manual edit, exclude graduate daytime placement and course-unavailable-room violations from the user-facing violation list and full validation/save blocking checks.

## Steps

1. Inspect manual edit conflict filtering for right-side violations, full validation, and save/export validation.
   - Verify: identify the shared filter path and any special-case validation paths.

2. Add manual-edit exclusion rules.
   - Verify: graduate daytime violations and course-unavailable-room violations are removed from manual edit visible/full validation conflicts.

3. Add focused tests.
   - Verify: manual edit does not show/block on graduate daytime placement or course-unavailable-room violations.

4. Run focused tests and WPF build.
   - Verify: relevant tests pass and WPF compiles.
