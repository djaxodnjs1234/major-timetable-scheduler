# Fix Grade Capacity Diagnostic for Section Hours

## Goal

Make the grade slot capacity diagnostics (`IE-038` / `GE-027`) match the solver's actual section-overlap rules. Same-course sections cannot overlap because of HC-08, so grade capacity must count every section's required slots instead of only the maximum required slots for the base course.

## Assumptions

- The user's reported saved timetable case should be diagnosed as grade slot capacity overflow before falling back to `GE-025`.
- Cross overlap still reduces only time that can intentionally overlap between different base courses.
- Same-base sections remain non-overlapping and therefore all consume grade time capacity.

## Steps

1. Inspect the existing grade capacity calculation and tests.
   - Verify: identify where same-base sections are collapsed.
2. Update the calculation to count section requirements correctly.
   - Verify: add regression coverage where same-base sections push a grade over capacity.
3. Run focused diagnostics tests and relevant ViewModel infeasible message tests.
   - Verify: tests pass and the expected diagnostic is `GE-027`.
4. Archive this plan.
   - Verify: move this file to `docs/implementation/archive/`.
