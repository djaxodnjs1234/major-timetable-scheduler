# Cross Block Structure Diagnostics

## Goal
When two WPF Cross courses have different block structures, block the Cross at add time and report a specific generation diagnostic for already-saved invalid Cross groups.

## Assumptions
- Only WPF behavior is in scope.
- Cross requires matching section count, total hours, and now matching effective block structure.
- The diagnostic should prevent the generic GE-025 fallback for this case.

## Steps
1. Add a Cross add-time block-structure validation in `DataInputViewModel`.
   - Verify: a ViewModel test confirms mismatched block structures are not saved as Cross.
2. Add a generation diagnostic in `TimetableDiagnostics`.
   - Verify: a solver diagnostic test confirms a saved invalid Cross reports a specific GE code/message.
3. Run targeted tests and WPF build.
   - Verify: relevant tests pass and WPF builds.
4. Archive this plan.
   - Verify: this file is moved to `docs/implementation/archive/`.
