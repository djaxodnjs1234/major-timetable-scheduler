# Screen 4 DND Move and Cross Duration Guard

## Goal
Implement and verify manual timetable drag-and-drop movement and restrict Cross creation to blocks with equal duration, without changing existing move, ForceMove, Swap, save/load, or RowSpan policies.

## Steps
1. Review current DND and Cross validation code.
   - Verify: identify existing entry points and ensure DND delegates to existing move validation.
2. Add focused ViewModel tests for DND move/drop behavior.
   - Verify: valid drop moves; invalid overlap/covered/cross-display-column drops do not mutate state or create undo snapshots.
3. Add Cross duration tests.
   - Verify: same-duration Cross remains allowed; 1h/2h duration mismatch hides/blocks Cross and does not mutate state.
4. Run WPF build and full test suite.
   - Verify: build succeeds and all tests pass.
5. Archive this plan after successful verification.
   - Verify: plan moved to docs/implementation/archive.
