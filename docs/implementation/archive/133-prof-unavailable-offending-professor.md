# Professor unavailable offending professor display

## Goal
Show only the professor whose unavailable slot is actually violated when rendering professor-unavailable conflicts.

## Assumptions
- A course may have a primary professor and coteachers, but only professors whose `UnavailableSlots` overlap the violating assignment should be shown.
- Existing concise validation/detail layout should stay unchanged except for replacing the broad professor list with the offending professor names.

## Steps
1. Inspect the current conflict text builders and tests.
   - Verify: identify the shared code paths for the full validation list and the right-side violation panel.
2. Add a helper that resolves violating professor names from the conflict assignments and professor unavailable slots.
   - Verify: right-side `사유` for `ProfUnavailable` includes only those names.
3. Use the helper in full validation and panel descriptions.
   - Verify: details no longer show non-violating coteachers.
4. Add/update focused tests.
   - Verify: tests cover a coteaching course where only one professor is unavailable.
5. Run the relevant tests/build and archive this plan.
   - Verify: selected tests pass and the WPF solution builds.
