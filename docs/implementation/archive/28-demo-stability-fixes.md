1. Fix room row save/delete to target the clicked room.
   - Verify: room buttons set `SelectedItem` before existing commands run.
2. Hide empty Inspector reason rows without changing Inspector layout.
   - Verify: existing reason booleans drive visibility and update on selection changes.
3. Include coteaching courses in professor-specific timetable filters.
   - Verify: main professor and coteaching professor both see the course.
4. Unify grade legend colors with timetable block colors.
   - Verify: legend uses the same `GradeToBrushConverter.BrushFor` source as blocks.
5. Run requested build and tests, then archive this plan.
   - Verify: both commands pass.
