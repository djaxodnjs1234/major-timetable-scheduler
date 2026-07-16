1. Inspect the manual edit screen and save path.
   - Verify: identify the log output widget and the block-add save handler.
2. Limit the manual edit log area height and make overflow scrollable.
   - Verify: the log area has a stable maximum size and scroll support.
3. Find why saving after adding a block clears existing course data.
   - Verify: reproduce or trace the data mapping that drops fields.
4. Fix block-add save so existing course data is preserved.
   - Verify: add or run a focused check that proves unrelated fields remain after save.
5. Run the available tests or app-level validation, then archive this plan.
   - Verify: command output confirms the changed paths still work.
