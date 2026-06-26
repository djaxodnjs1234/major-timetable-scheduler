# Block Structure Clears Fixed Checkbox

## Assumptions

- The ViewModel already clears fixed-time data when block structure changes.
- The requested fix is for the visible `시간 고정` checkbox to refresh immediately after the block-structure combo changes.
- The smallest safe change is to reuse the existing fixed-time checkbox refresh used by weekly-hours changes.

## Steps

1. Update block-structure change handling in the WPF code-behind.
   - Verify: the handler calls the same fixed checkbox refresh used by hours changes.

2. Add a focused test for the UI refresh wiring.
   - Verify: source-level WPF test confirms the handler refreshes the checkbox.

3. Run focused tests and build.
   - Verify: relevant tests and `dotnet build` pass.

4. Archive this plan after completion.
   - Verify: move this file to `docs/implementation/archive/`.
