# Manual conflict panel readability

## Goal

Make the right-side manual edit constraint panel easier to read by removing
time/block details from reason and course lines, and by using neutral text
colors for the involved course names.

## Assumptions

- This request targets the right-side `ConflictGroups` panel in manual edit,
  not the full validation dialog.
- Conflict detection and grouping behavior should not change.
- The panel title may still identify the clicked timetable location; the
  expanded line items should omit duplicated time text.

## Plan

1. Simplify right-panel line text.
   - Verify: `사유:` values contain only the conflict label, and course lines
     contain only course/section labels.
2. Adjust right-panel course text colors.
   - Verify: `위반 블럭` values render black and `관련 수업` values render with a
     lighter gray, without grade color binding.
3. Update focused tests for the new text and style behavior.
   - Verify: tests assert no time text in `위반 블럭`, `관련 수업`, or `사유`.
4. Run focused tests and build, then archive this plan file.
   - Verify: targeted tests and solution build pass.
