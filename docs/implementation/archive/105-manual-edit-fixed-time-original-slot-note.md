# Manual Edit Fixed Time Original Slot Note

## Goal

When a fixed-time course is moved in manual edit, show a non-blocking exception/warning that tells the user where the course was originally fixed.

## Steps

1. Inspect manual edit conflict filtering and fixed-time violation descriptions.
   - Verify: identify why fixed-time violations are currently hidden.

2. Show fixed-time violations as manual edit warnings, not blocking errors.
   - Verify: moving a fixed-time course creates a visible warning but does not block save/export.

3. Include original fixed slots in the warning description.
   - Verify: the message includes the current position and original fixed position/range.

4. Add a focused test.
   - Verify: moving a fixed-time course surfaces one fixed-time warning with the original slot.

5. Run focused tests and WPF build.
   - Verify: relevant tests pass and WPF compiles.
