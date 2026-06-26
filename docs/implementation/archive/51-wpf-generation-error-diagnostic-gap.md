# WPF Generation Error Diagnostic Gap

## Assumptions

- The WPF generation path is the target; the Python prototype is out of scope.
- Existing IE/GE diagnostics should not be duplicated under a new ID.
- A good fix is one clear, reproducible infeasible-input case that reports a specific reason before or during generation.

## Steps

1. Audit current WPF input and generation diagnostics.
   - Verify: identify existing covered cases and avoid adding a duplicate diagnostic.

2. Find one definite uncovered failure case.
   - Verify: reproduce it with a focused failing test or a small solver fixture.

3. Add the narrowest diagnostic and user-facing message.
   - Verify: the new case reports a stable IE/GE reason instead of a generic failure.

4. Add or update focused regression tests.
   - Verify: tests fail before the fix and pass after it.

5. Run relevant tests/build and archive this plan.
   - Verify: targeted tests and WPF build pass, then move this file to `docs/implementation/archive/`.
