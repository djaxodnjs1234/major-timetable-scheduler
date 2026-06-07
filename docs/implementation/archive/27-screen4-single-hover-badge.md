1. Add one active hover badge state to `UnifiedTimetableControl`.
   - Verify: same target and badge kind does not recreate buttons.
2. Clear the previous badge before showing a badge on a new hover or drag target.
   - Verify: only one course cell keeps `+` / swap badges at a time.
3. Make badge clearing scan the rendered grid so stale badge buttons are removed.
   - Verify: Rebuild, drop, drag end, empty target, and mouse leave clear leftovers.
4. Run the requested WPF build and test commands, then archive this plan.
   - Verify: both commands pass.
