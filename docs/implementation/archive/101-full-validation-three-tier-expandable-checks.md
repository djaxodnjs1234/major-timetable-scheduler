# Full Validation Three-Tier Expandable Checks

## Goal

Improve the manual edit full validation dialog so it behaves like a final checklist: show every validation constraint, group checks into three importance tiers, and allow each abnormal check to expand and reveal the related error details.

## Steps

1. Extend the validation check data contract.
   - Verify: a check can carry tier information and matching conflicts without changing existing conflict confirmation flows.

2. Build full validation checks from all manual validation conflict types.
   - Verify: hidden manual-edit conflict types still appear in the full validation checklist, while the right-side conflict list remains unchanged.

3. Render validation checks as grouped expandable rows in the WPF dialog.
   - Verify: checks are grouped by tier and abnormal rows can expand to show related conflict details.

4. Add focused tests for tier coverage and conflict attachment.
   - Verify: full validation passes all check rows to the dialog and includes abnormal detail conflicts.

5. Run focused tests and build.
   - Verify: relevant ViewModel tests pass and the WPF app compiles.
