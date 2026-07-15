# Manual edit all conflicts as errors

## Assumptions

- The user wants the manual edit constraint panel and full validation dialog to stop using yellow warning severity.
- Existing warning-only manual edit conflicts, especially professor unavailable and fixed-time deviation, should now be treated as red errors.
- Soft move-preview warnings can remain as movement hints only if they are not part of the constraint/error panels, but any persisted constraint conflict should be an error.

## Plan

1. Inspect warning classification and save/validation behavior.
   - Verify: identify the function that converts conflicts to warnings and tests that assert warning behavior.
2. Remove manual edit warning downgrades and count validation items as errors only.
   - Verify: fixed-time and professor-unavailable conflicts surface with `ConflictSeverity.Error`.
3. Update focused tests for the new all-error behavior.
   - Verify: warning-specific assertions now expect errors and no warning counts.
4. Run focused tests and a solution build, then archive this plan.
   - Verify: report exact commands and results.
