# Full Validation Check Summary Only

## Goal

Restore the manual edit full validation popup so it shows validation check rows with normal/abnormal status, instead of detailed violation cards.

## Steps

1. Adjust the validation dialog rendering when check rows are supplied.
   - Verify: full validation shows only check rows, while blocking/export dialogs can still show detailed conflicts.

2. Update focused tests for the validation dialog contract if needed.
   - Verify: validation check rows are still passed by the ViewModel.

3. Run focused tests and build.
   - Verify: relevant ViewModel tests pass and WPF compiles.
