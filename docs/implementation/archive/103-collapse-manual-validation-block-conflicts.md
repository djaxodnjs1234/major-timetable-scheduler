# Collapse Manual Validation Block Conflicts

## Goal

Update manual edit conflict displays so repeated per-period violations from the same multi-hour block are shown once per block. In full validation details, show each problem as `location / block name`.

## Steps

1. Inspect the manual conflict display and full validation detail builders.
   - Verify: identify the shared path where conflict lines and validation check details are generated.

2. Add block-level grouping for per-period conflicts.
   - Verify: a three-hour block with the same violation produces one display item/detail instead of three hourly items.

3. Format full validation detail lines as `location / block name`.
   - Verify: expanded validation details use the requested order.

4. Add focused tests for the right-side violation display and full validation detail output.
   - Verify: tests fail before/fix and pass after implementation.

5. Run focused tests and WPF build.
   - Verify: relevant tests pass and XAML still compiles.
