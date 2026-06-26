# Cross Uncheck And Cancel Without Exception

## Goal
Allow selected Cross candidates to be unchecked after two selections, and make solve cancellation complete without surfacing `OperationCanceledException`.

## Assumptions
- Two selected Cross candidates should remain enabled so the user can uncheck either one.
- Unselected Cross candidates should be disabled while two candidates are selected.
- Cancellation is a normal user action and should produce a cancelled result/status without throwing an exception out of solver execution.

## Steps
1. Update Cross candidate enablement.
   - Verify: after two selections, selected candidates are enabled and unselected candidates are disabled.
2. Change solver cancellation to return a cancelled result rather than throwing `OperationCanceledException`.
   - Verify: the ViewModel shows cancelled status and no exception escapes the command.
3. Add or update tests for both behaviors.
   - Verify: Cross manager and cancellation tests pass.
4. Run targeted tests and WPF build.
   - Verify: relevant tests pass and WPF builds.
5. Archive this plan after verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
