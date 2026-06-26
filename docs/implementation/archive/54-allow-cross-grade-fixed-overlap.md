# Allow Cross-Grade Fixed-Time Overlap

## Goal

Allow fixed-time courses from different grades to use the same day and period when they do not violate real scheduling constraints such as same professor, same course section group, same grade, or same fixed room.

## Assumptions

- Different-grade fixed-time overlap is allowed only when the solver can still assign valid rooms.
- Same-grade fixed-time overlap should remain blocked.
- Same-professor, same-base course sections, and same fixed-room conflicts should remain blocked.
- This change targets WPF information input validation, not the solver hard constraints.

## Steps

1. Inspect the existing fixed-time overlap validation and tests.
   - Verify: identify the exact method and current test expectations.
2. Update the validation to ignore harmless cross-grade overlaps.
   - Verify: add or update tests covering allowed cross-grade overlap and still-blocked conflicts.
3. Run focused tests for course-group fixed-time behavior and diagnostics.
   - Verify: all targeted tests pass.
4. Archive this plan after verification.
   - Verify: move this file to `docs/implementation/archive/`.
