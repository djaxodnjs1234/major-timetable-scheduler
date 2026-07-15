# Retake validation block details

## Goal

Show retake validation errors per conflicting retake block, with readable
multi-line details that separate the retake target from the overlapping
required-major course.

## Assumptions

- This request targets the full-validation `재수강생 고려` details.
- Retake conflicts should still only appear when no retake section avoids the
  current grade required-major courses.
- A block can be represented by the assignment identity already assigned by
  the manual edit ViewModel.

## Plan

1. Split retake validation conflicts by retake assignment block.
   - Verify: multiple retake sections produce separate detail conflicts.
2. Format each retake detail with newline-separated labels.
   - Verify: detail text starts with `재수강 대상:` and includes a separate
     `겹치는 전필:` line.
3. Update focused tests for per-block detail output.
   - Verify: tests assert separate conflict items and newline formatting.
4. Run focused tests and build, then archive this plan.
   - Verify: targeted tests and solution build pass.
