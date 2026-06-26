# Graduate Academic Level Support

## Goal
Add graduate courses as a fifth academic level while preserving the existing integer `Course.Grade` storage model. Graduate courses use internal value `5` and display as `대학원`.

## Steps

1. Add shared academic-level metadata.
   - Verify: all grade display names come from one helper and `5` displays as `대학원`.

2. Update data input surfaces.
   - Verify: course grade selection supports graduate without showing raw `5`.

3. Update timetable views, legends, manual editing, and export.
   - Verify: unified and grade-specific timetables include graduate columns/tabs/labels.

4. Update Excel grade parsing.
   - Verify: `대학원`, `석사`, `박사`, `graduate`, `grad`, and numeric `5` load as graduate.

5. Update tests and documentation.
   - Verify: targeted tests pass and documentation describes `1~4학년 + 대학원`.

6. Run build/tests and archive this plan.
   - Verify: build succeeds; known unrelated test failures are documented if they remain.
