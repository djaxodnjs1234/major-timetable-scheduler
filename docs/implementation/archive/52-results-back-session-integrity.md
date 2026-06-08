## Goal

Fix professor-field loss when navigating from results preview back to data input, and preserve course/professor metadata integrity across session and DB-backed flows.

## Plan

1. Trace the results-to-input return path and identify where session or workspace state is replaced.
   - Verify: pinpoint the exact transition that causes course professor inputs to become blank.

2. Patch the state-management boundary so returning from results keeps the same course/professor metadata that produced the shown solutions.
   - Verify: data input shows the original professor assignments after going to results and back.

3. Add regression tests for results->back->input and related session/global integrity behavior.
   - Verify: tests fail if professor metadata is lost or if session data bleeds incorrectly into the global DB-backed workspace.

4. Build, run focused tests, and exercise the flow through a driver.
   - Verify: result preview, back navigation, and saved data all preserve professor metadata without blank fields.
