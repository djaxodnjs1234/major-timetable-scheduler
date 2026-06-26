# Hide single-section labels

1. Identify the timetable card data path that decides whether to render a section label.
   - Verify: a course with one section renders without an A label.
2. Preserve A/B labels for course groups with multiple sections across all timetable views.
   - Verify: multi-section courses retain their existing labels.
3. Add focused rendering tests and run isolated build/tests, then archive this plan.
   - Verify: card title formatting and WPF build pass.
