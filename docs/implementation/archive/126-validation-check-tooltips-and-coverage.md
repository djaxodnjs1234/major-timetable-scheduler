# Validation check tooltips and coverage

## Assumptions

- Each full-validation row should have a one-line tooltip explaining what that check means.
- The tooltip should come from the ViewModel data so the WPF dialog only renders it.
- Full validation should include manual-edit-relevant conflict types that the detector can already produce but the check list currently omits.

## Plan

1. Compare full-validation check definitions against `ConflictType` and existing manual-edit filters.
   - Verify: identify omitted conflict types that can appear in manual edit validation.
2. Add a one-line tooltip/description to every validation check item.
   - Verify: tests assert every check has a tooltip and representative text.
3. Add missing full-validation rows for relevant omitted conflict types.
   - Verify: tests cover newly included rows, especially course unavailable room and fixed room/block start/lunch candidates.
4. Run focused tests and a solution build, then archive this plan.
   - Verify: report exact commands and results.
