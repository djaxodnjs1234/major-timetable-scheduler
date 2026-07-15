# Simplify full validation check list

## Assumptions

- The requested removals apply to the "전체검증" dialog/check list, not to the manual edit cell overlay or conflict detector internals.
- Removed validation categories should also be excluded from the full-validation summary counts, so the result does not report hidden conflicts.
- The remaining check rows should appear as one flat list without 1/2/3 tier grouping labels.

## Plan

1. Exclude the requested conflict types from full-validation results and check rows.
   - Verify: removed categories no longer appear in validation check definitions or summary counts.
2. Flatten the validation dialog display so it does not group rows by tier.
   - Verify: the validation dialog renders one list of checks.
3. Update focused tests around full validation.
   - Verify: tests assert removed categories are absent and remaining categories still display.
4. Run focused tests and WPF build, then archive this plan.
   - Verify: test/build commands pass.
