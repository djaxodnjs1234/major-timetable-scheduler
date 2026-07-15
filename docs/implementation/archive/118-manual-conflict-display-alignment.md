# Manual Conflict Display Alignment

## Goal

Align the manual edit violation display rules across three surfaces:

- small red text inside timetable blocks
- right-side violation list
- full validation

Also document the mapping in a markdown file and remove remaining "professor unavailable room" behavior/data from code paths that still expose it.

## Assumptions

- The timetable block's small red text is the source of truth for which violation types should be visible in manual edit.
- Right-side violations and full validation should use the same visible violation categories, with different detail levels only.
- "Professor unavailable room" is deprecated and should not be shown, validated, or persisted from code-created data.

## Plan

1. Inspect current conflict types, badge text, right-side list, and full validation check generation.
   - Verify: identify the single mapping point or smallest set of mapping points to update.
2. Create a markdown document for the violation display table.
   - Verify: document contains columns for violation item, timetable small text, right-side violations, and full validation.
3. Remove/deactivate professor unavailable room remnants.
   - Verify: no manual edit/full validation output includes professor unavailable room, and seed/test data is cleared or adjusted.
4. Align right-side violations and full validation to the timetable badge-visible categories.
   - Verify: focused tests cover removed professor unavailable room and matching display categories.
5. Run focused tests and WPF build.
   - Verify: relevant tests and build pass.
6. Archive this plan when complete.
   - Verify: plan file is moved to `docs/implementation/archive/`.
