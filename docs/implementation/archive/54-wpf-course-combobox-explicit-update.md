# WPF Course ComboBox Explicit Update

1. Protect shared string ComboBox bindings.
   - Verify: professor and course type bindings only update their source after a valid user selection.

2. Flush pending shared selections before save.
   - Verify: the save action commits the current professor and course type before synchronizing sections.

3. Add rendered-view regression coverage.
   - Verify: rebuilding the course list preserves single-section and multi-section professor values, while valid user selections still save.

4. Run focused WPF tests and solution build.
   - Verify: data-input UI tests, course-group tests, and the WPF solution build pass.

5. Archive this implementation plan.
   - Verify: move this file to `docs/implementation/archive/` after validation.
