# WPF Course Group Professor Preservation

1. Inspect WPF course group editing flow.
   - Verify: identify every WPF path that reads `Sections[0]`, rebuilds course groups, saves groups, or navigates to preview/manual edit.

2. Add representative course-level fallback logic.
   - Verify: grouped display chooses a non-empty professor from any section when section A is empty.

3. Harden `SaveGroup()` synchronization.
   - Verify: saving a group never copies an empty section A professor over another section's valid professor, and logs inconsistent non-empty professor values.

4. Add temporary professor trace logs.
   - Verify: logs print per-section professor values before/after rebuild, save, and preview/manual navigation handoff.

5. Add WPF regression tests.
   - Verify: tests cover section A empty / section B valid professor fallback, save promotion, and inconsistent professor detection behavior.

6. Run WPF build/tests and archive this plan.
   - Verify: relevant build/tests pass, then move this file to `docs/implementation/archive/`.
