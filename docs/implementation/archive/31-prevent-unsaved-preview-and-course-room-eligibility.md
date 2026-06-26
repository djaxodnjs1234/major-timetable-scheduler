# Prevent unsaved preview navigation and make room eligibility course-specific

1. Prevent navigating to solution preview while information-input editors have unsaved changes.
   - Verify: the preview event is not raised and the input error explains which editor must be completed or cancelled.
2. Replace the professor-wide automatic-room consistency constraint with per-course professor room eligibility.
   - Verify: two courses taught by the same professor can use opposite rooms when each course disallows the other room; professor allowed/unavailable rooms still restrict automatic assignment, while fixed rooms retain course priority.
3. Add regression tests and run the WPF test suite.
   - Verify: the new tests and existing WPF projects build and pass.
