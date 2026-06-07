# Unified timetable stale interaction state

1. Add a minimal control-local interaction reset.
   - Verify: rebuild clears drag source/start state and a short drop-click guard blocks stale click re-entry.
2. Validate event args against the current rendered grid state.
   - Verify: cell click, drag source, hover, drop, and badge events ignore stale tags after rebuild.
3. Preserve ViewModel movement policies.
   - Verify: click/DND/force move still end with selection cleared; failed moves keep ViewModel selection.
4. Add focused regression coverage where possible without adding WPF test references.
   - Verify: ViewModel tests continue to assert moved blocks, old positions, and cleared move states.
5. Run required build and tests, then archive this plan.
   - Verify: requested dotnet build and dotnet test commands pass.
