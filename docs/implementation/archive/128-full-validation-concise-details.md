# Full validation concise details

## Goal

Make expanded full-validation details concise by removing repeated location
text from detail lines and simplifying retake conflict wording.

## Assumptions

- The full-validation tooltip explains the meaning of each check, so expanded
  error details should only list the involved resource and courses.
- The location line remains the only place where day and period are shown in
  full-validation details.
- Manual edit side-panel behavior should stay unchanged except for shared
  user-facing conflict descriptions where existing tests already assert the
  readable label.

## Plan

1. Rewrite conflict detail builders to omit location text and put resource
   before courses where applicable.
   - Verify: room and professor conflict details no longer include day/period.
2. Simplify retake validation detail text to concise involved-course labels.
   - Verify: retake detail no longer contains explanatory prose or "safe
     section" wording.
3. Keep full-validation expanded details ordered as location, reason, detail,
   then involved course blocks.
   - Verify: focused WPF source test still covers the order.
4. Run focused tests and build, then archive this plan.
   - Verify: targeted tests and solution build pass.
