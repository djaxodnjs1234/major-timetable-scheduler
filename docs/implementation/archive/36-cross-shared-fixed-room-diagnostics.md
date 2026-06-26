# Reject Cross groups with shared fixed rooms

1. Detect Cross-paired sections that share a fixed room.
   - Verify: input diagnostics report `IE-039` and generation diagnostics report `GE-028`.
2. Reject the invalid Cross immediately from information input.
   - Verify: no Cross group is stored and the status includes `IE-039`.
3. Run focused diagnostics and course-group tests, then build WPF.
   - Verify: regression tests and the application build pass.
