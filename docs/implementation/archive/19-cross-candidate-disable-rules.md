# Cross Candidate Disable Rules

## Goal
Update WPF Cross candidate selection so single-section courses are disabled and all candidates become disabled once two courses are selected.

## Assumptions
- Cross should only be created for course groups with at least two explicit sections.
- Once two candidates are selected, the UI should freeze all candidate checkboxes until the Cross is added or the manager is rebuilt.
- Programmatic command execution should also reject single-section candidates as a defensive check.

## Steps
1. Update Cross candidate enablement rules.
   - Verify: one-section candidates are disabled and two selected candidates disable every candidate.
2. Add command-level validation for single-section selections.
   - Verify: `AddCross` does not create invalid Cross groups if a disabled item is toggled programmatically.
3. Update/add Cross manager tests.
   - Verify: existing Cross tests use two-section course groups where Cross creation is expected.
4. Run targeted tests and WPF build.
   - Verify: relevant tests pass and WPF builds.
5. Archive this plan after verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
