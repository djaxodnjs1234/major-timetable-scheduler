# Saved Cross Suppress Validation

1. Trace initial load conflict refresh and Cross cleanup paths.
   - Verify: identify where HC-11 can appear or saved links can be removed after restore.
2. Add explicit initial-load suppression state and diagnostics.
   - Verify: tests can assert suppression, cleanup skip reason, saved/restored/ignored counts.
3. Suppress only HC-11 for restored Cross pairs before user edit.
   - Verify: HC-20 and professor/room conflicts remain visible; save validation remains strict.
4. Re-enable cleanup only after a real edit succeeds.
   - Verify: movement/Cross/Swap/room changes clear suppression.
5. Run build/tests and archive plan.
   - Verify: WPF build and full test suite succeed.
