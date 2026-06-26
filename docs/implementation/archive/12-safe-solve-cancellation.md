# Safe Solve Cancellation

## Goal
Canceling timetable generation from the data input screen must stop gracefully without surfacing an unhandled exception or crashing the app.

## Assumptions
- `DiverseSolver` may still use `OperationCanceledException` internally.
- UI-facing code should treat cancellation as a normal result and set the status to canceled.
- The cancel command should be safe even if cancellation is requested more than once.

## Steps
1. Add a regression test for canceling `DataInputViewModel.SolveCommand`.
   - Verify: cancellation completes without throwing and sets `StatusMessage` to canceled.
2. Harden `DataInputViewModel` cancellation handling.
   - Verify: `IsSolving` is reset, command state refreshes, and no stale `CancellationTokenSource` remains active.
3. Run targeted tests.
   - Verify: the new cancellation test and existing solver cancellation test pass.
4. Archive this plan.
   - Verify: this file is moved to `docs/implementation/archive/`.
