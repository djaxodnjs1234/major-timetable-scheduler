# Full validation detail readability

## Goal

Make full-validation error rows easier to scan by showing the location first,
moving the conflicting course/section information to the end, and simplifying
the validation row summary to a single inline count.

## Assumptions

- The request applies to the full validation dialog rows and expanded details,
  not the right-side manual edit conflict panel.
- Existing conflict detection behavior should not change; this is a display
  and wording update.
- Retake validation should avoid the phrase "safe section" and explain that
  required-major versus required-major overlap is the error.

## Plan

1. Update validation row summary data and WPF header layout.
   - Verify: full-validation rows can show the check name and a light `N건`
     count on one line without the extra detail line.
2. Reorder expanded full-validation conflict blocks to show location first,
   then reason/detail, then conflicting courses and sections.
   - Verify: focused tests assert location appears before course labels.
3. Rewrite retake conflict text to explain required-major overlap and list
   location plus involved courses.
   - Verify: focused tests assert the new wording and absence of "safe section".
4. Run focused tests and build, then archive this plan file.
   - Verify: targeted tests and solution build pass.
