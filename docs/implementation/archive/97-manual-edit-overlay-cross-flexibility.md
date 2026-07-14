# Manual Edit Overlay and Cross Flexibility

## Goal

Refine manual editing behavior so visual warnings are less noisy and manual Cross links are more flexible.

## Steps

1. Normalize red move-state visuals.
   - Verify: WPF project builds and move-state colors share one red background.

2. Remove manual-edit soft warnings for Monday morning and Friday afternoon.
   - Verify: existing manual edit tests pass or compile after the policy change.

3. Stop painting red move overlays over existing non-selected course blocks.
   - Verify: add/update UI test coverage for occupied course cells.

4. Allow manual Cross links with three or more assignments and unequal block lengths.
   - Verify: add/update ViewModel tests for multi-Cross and unequal-duration Cross creation paths where practical.

5. Run relevant WPF/ViewModel tests and build.
   - Verify: report passing checks and any environment-specific UI test blockers.
