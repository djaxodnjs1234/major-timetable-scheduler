# Unconditional Saved Cross Restore

1. Inspect current saved Cross restore conditions.
   - Verify: identify every condition that can drop a saved link during initial load.
2. Change restore to trust saved links when both referenced course assignments exist.
   - Verify: stale saved slots and non-overlapping current positions still keep the link pair.
3. Keep HC-11 exception range-based and keep all non-HC-11 conflicts active.
   - Verify: HC-20 and missing ManualCrossLink save blocking tests pass.
4. Run build and tests, then archive this plan.
   - Verify: WPF build and full test suite succeed.
