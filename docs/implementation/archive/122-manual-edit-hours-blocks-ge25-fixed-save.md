# Manual edit hours, block options, GE-25 messaging, and fixed-slot save

## Assumptions

- Manual edit course/block controls should reuse the same weekly-hour and block-structure choices as the information input page.
- GE-25 should describe likely tight constraints and suggest which constraints to relax, while system or unexpected solver errors should use a separate failure message.
- Clearing fixed time should allow saving even if the previous fixed slots overlapped before the user cleared them.

## Plan

1. Inspect manual edit block-add/edit controls and the information-input block option source.
   - Verify: identify one shared option source or a minimal adapter.
2. Change manual edit weekly hours to a 1-5 dropdown and block structure options to match information input for the selected hours.
   - Verify: add or update viewmodel tests for available choices and state updates.
3. Split GE-25 user guidance from system/unexpected solver failures.
   - Verify: tests assert tight-constraint guidance and non-infeasible/system failure wording separately.
4. Fix fixed-slot save after clearing overlaps.
   - Verify: add a regression test where overlapping fixed slots fail first, then clearing fixed time saves successfully.
5. Run focused tests and a solution build, then archive this plan.
   - Verify: report the exact commands and results.
