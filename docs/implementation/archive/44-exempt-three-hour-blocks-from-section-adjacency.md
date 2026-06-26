# Exempt three-hour blocks from section adjacency

1. Update HC-15 so same-professor sections remain adjacent for one- and two-hour blocks only.
   - Verify: three-hour blocks have no HC-15 adjacency constraint.
2. Remove the obsolete pre-solve diagnostic for impossible three-hour section adjacency.
   - Verify: valid `2+3` section pairs do not report GE-030 solely because of the three-hour block.
3. Add focused solver and diagnostic tests, then build and test with an isolated output directory.
   - Verify: two-hour adjacency remains enforced and three-hour blocks are schedulable independently.
