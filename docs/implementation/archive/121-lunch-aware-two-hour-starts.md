# Lunch-aware two-hour block starts

## Assumptions

- A two-hour block may start at any period where two actual instructional periods are available back-to-back.
- Static lunch modes remove only the configured lunch period from those candidate spans.
- Flexible lunch mode should allow starts that are valid under at least one of the two lunch choices, while still preventing a block from occupying both lunch candidates at once.
- Existing fixed-slot editor coercion should keep protecting imported or saved values that no longer match the active lunch policy.

## Plan

1. Update the shared block-start rule so two-hour starts use all consecutive valid periods instead of pair-only starts.
   - Verify: lunch policy unit tests cover no lunch, period-4 lunch, period-5 lunch, and flexible lunch.
2. Align fixed-time editor expectations with the new starts.
   - Verify: fixed slot editor tests assert the visible period options for all lunch modes.
3. Align manual conflict validation with the shared block-start rule.
   - Verify: focused solver/viewmodel tests pass.
4. Run focused tests and a solution build.
   - Verify: report pass/fail status and archive this plan when done.
