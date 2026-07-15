# Manual fixed block violation scope

## Assumptions

- For a fixed course split as 2+1, moving only the 1-hour block should report a fixed-time violation only for that moved block.
- Unmoved fixed blocks from the same course must stay valid when their current slots still match one fixed block run.
- Other grade blocks sharing the visual area should not be pulled into the fixed-time violation unless that exact assignment belongs to the moved fixed course block.

## Plan

1. Inspect manual edit fixed-time conflict detection and block collapsing.
   - Verify: identify whether validation compares assignments against the whole fixed slot set instead of block runs.
2. Add a regression test for a 2+1 fixed course where only the 1-hour block is moved.
   - Verify: the test fails before the fix or proves the current behavior shape.
3. Narrow fixed-time violation detection to the exact moved fixed block run.
   - Verify: the 2-hour fixed run and unrelated grade assignment are not included in the violation.
4. Run focused tests and a solution build, then archive this plan.
   - Verify: report exact commands and results.
