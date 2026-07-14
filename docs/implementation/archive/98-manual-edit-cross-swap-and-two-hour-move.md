# Manual Edit Cross Swap and Two-Hour Move Fix

## Goal

Enable swap actions on Cross-linked blocks and fix two-hour block moves that fail with an assignment-count mismatch.

## Steps

1. Inspect swap hover/drop eligibility for Cross-linked blocks.
   - Verify: Cross-linked occupied cells can return a swappable hover/drop state when a selected block exists.

2. Relax block assignment identity matching for multi-period visual blocks.
   - Verify: a two-hour visual block can resolve to its two underlying assignments even after Cross/manual render changes.

3. Add focused tests for Cross swap and two-hour move resolution.
   - Verify: tests fail before the fix or cover the reported path after the fix.

4. Build and run focused tests.
   - Verify: WPF/ViewModel projects compile and focused tests pass.
