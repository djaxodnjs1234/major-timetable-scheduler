# Manual edit course delete scope and block add rules

## Assumptions

- "Delete whole course" means deleting every timetable/staging block for the selected subject across sections, not just the selected section/CourseId.
- Course-level identity should use the resolved `Course` family first (`DomainHelpers.BaseId` or normalized course name), with `CourseId` as a fallback.
- Block structure is optional and disabled by default; when disabled, the added course uses one block equal to the selected weekly hours.
- When enabled, block structure is selected from valid dropdown options generated from the selected weekly hours.

## Plan

1. Change whole-course deletion from same `CourseId` to same course family.
   - Verify: deleting an A-section block removes A/B sections of the same course while leaving unrelated courses.
2. Add optional block-structure selection state and generated structure options based on class hours.
   - Verify: default is disabled; enabling exposes valid structures for the selected hours.
3. Restrict section count to a maximum of 9.
   - Verify: inputs above 9 clamp to 9 and generated staged blocks do not exceed 9 sections.
4. Reject manual course names containing spaces.
   - Verify: block add fails with a clear message when the course name includes whitespace.
5. Update the WPF advanced settings UI from free-text block structure to a disabled-by-default dropdown.
   - Verify: WPF build succeeds.
6. Run focused manual edit tests and WPF build, then archive this plan.
   - Verify: tests/build pass.
