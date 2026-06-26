# Implement Error Case Diagnostics

## Assumptions

- The documentation IDs are the source of truth for new validation and diagnostic messages.
- Pre-solve validation should stop obviously invalid input before OR-Tools runs.
- Solver failure diagnostics should include stable `GE-###` IDs so tests can assert the reason.
- Existing save-only edit behavior must remain unchanged.

## Steps

1. Add a shared diagnostic helper for input and generation errors.
   - Verify: unit tests can call the helper without WPF dependencies.

2. Wire pre-solve validation into `DataInputViewModel`.
   - Verify: invalid input sets `StatusMessage` with the relevant `IE-###` ID and does not invoke the solver.

3. Improve infeasible/no-solution messages with `GE-###` IDs.
   - Verify: infeasible test fixtures report actionable IDs.

4. Add focused regression tests for representative IE/GE cases.
   - Verify: tests cover empty names, unsaved edits, no rooms, fixed professor/room conflicts, and retake conflicts.

5. Run build/tests and archive this plan.
   - Verify: `dotnet build` and relevant `dotnet test` pass, then move this file to `archive`.
