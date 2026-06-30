# Manual Edit School Fixed Validation Fix

1. Reproduce and locate the manual-edit save validation path.
   - Verify: identify where saved/manual assignments are compared against fixed courses.
2. Exclude school fixed courses from real-assignment conflict detection while keeping normal fixed courses checked.
   - Verify: school fixed courses no longer create fixed-time violations when absent from assignments, and regular fixed courses still do.
3. Add focused regression tests for manual-edit conflict detection/save behavior.
   - Verify: tests fail before the fix and pass after it.
4. Run focused build/tests and archive this plan.
   - Verify: WPF project builds via alternate output path and relevant tests pass.
