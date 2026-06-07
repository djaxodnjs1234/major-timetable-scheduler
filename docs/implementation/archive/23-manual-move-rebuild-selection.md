# Manual move rebuild and selection cleanup

1. Trace post-move state updates and grid rebuild rendering.
   - Verify: identify whether stale selected cell state or stale CellAssignment references can be reintroduced after `_working` changes.
2. Fix post-move ordering without changing validation policy.
   - Verify: click, DND, and force move update `_working`, rebuild grid, run existing cleanup/conflict work, then clear selection as the final UI state change.
3. Add focused regression tests for grid blocks and stale selection state.
   - Verify: moved one-hour/two-hour/force-moved blocks render at new positions, old positions are empty, and move states do not survive success; failure preserves selection/grid.
4. Run required build and tests, then archive this plan.
   - Verify: requested dotnet build and dotnet test commands pass.
