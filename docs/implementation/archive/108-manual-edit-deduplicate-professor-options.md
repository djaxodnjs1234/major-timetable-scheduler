# Manual Edit Deduplicate Professor Options

## Goal

In manual edit, show each professor only once in primary and co-teaching professor dropdowns even when the same display name exists with multiple internal IDs.

## Cause

Professor conflicts are detected by professor ID. If two professor records have the same displayed name but different IDs, selecting the other duplicate makes the system treat it as a different professor and the conflict can disappear.

## Steps

1. Inspect current professor option construction and selected/coteach option lists.
   - Verify: duplicate display names can produce multiple options.
2. Deduplicate professor options by normalized display name, preferring an ID already used by the selected course when applicable.
   - Verify: dropdown options contain a single option per visible professor name.
3. Keep selected co-teaching display list deduplicated by display name too.
   - Verify: co-teaching professor list does not show visually duplicated names.
4. Add focused ViewModel test for duplicated professor names.
   - Verify: only one visible professor option appears and selecting it keeps conflict identity stable.
5. Run focused tests and WPF build, then archive this plan.

