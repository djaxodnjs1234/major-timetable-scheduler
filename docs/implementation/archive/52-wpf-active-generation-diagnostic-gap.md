# WPF Active Generation Diagnostic Gap

## Assumptions

- Only currently reachable WPF information-input behavior counts.
- Stale code paths that remain in models but are not exposed by the UI should not justify a new diagnostic.
- The first correction is to remove the invalid `AllowedRooms`-based candidate from the current work.

## Steps

1. Remove the invalid professor allowed-room diagnostic candidate.
   - Verify: no new `IE-041` or `GE-032` diff remains.

2. Audit active WPF input paths for course, professor, room, Cross, and solve options.
   - Verify: the next candidate uses only fields that users can actually set.

3. Reproduce one definite uncovered generation failure.
   - Verify: a focused solver or ViewModel test shows the solver fails while current diagnostics miss the cause.

4. Add the narrow diagnostic and regression test.
   - Verify: the new test reports a specific ID instead of a generic failure.

5. Run focused tests/build and archive this plan.
   - Verify: relevant tests and WPF build pass, then move this file to `archive/`.
