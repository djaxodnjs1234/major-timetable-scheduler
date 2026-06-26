# WPF Generation Editing Diagnostics

## Goal
Fix three WPF-only behaviors: generation cancellation should be available while solving, course checklist edits should save cleared values, and infeasible results should show actionable diagnostics instead of an unavailable-detail fallback.

## Assumptions
- The Python prototype is out of scope for this task.
- Existing WPF cancellation support should be reused and only tightened where the UI or command state is incomplete.
- If precise infeasible diagnostics cannot identify one conflicting constraint, the UI should still show concrete fields to review.

## Steps
1. Verify the WPF solve cancellation path and adjust the button/command state if needed.
   - Verify: targeted ViewModel tests cover cancellation and the cancel button is bound to the command.
2. Flush course checklist selections before saving group or section edits.
   - Verify: tests cover clearing team-teaching professors, unavailable rooms, and fixed rooms.
3. Replace the infeasible fallback message with actionable diagnostics.
   - Verify: tests cover infeasible status with no detailed diagnostic match.
4. Run WPF build/tests.
   - Verify: relevant `dotnet test`/`dotnet build` commands pass.
5. Archive this plan after verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
