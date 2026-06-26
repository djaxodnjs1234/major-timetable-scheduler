# Grade Slot Capacity Diagnostics

## Goal
Show a specific WPF diagnostic when one academic level has more required class slots than the timetable can contain.

## Assumptions
- WPF is the target surface for this change.
- One academic level can use at most `Days * ValidPeriods.Count` slots.
- Same-grade Cross pairs may share slots, so the capacity check should subtract the overlap that Cross intentionally allows.
- This diagnostic is a necessary-condition check. It catches clear capacity overflow, not every possible infeasible layout.

## Steps
1. Add grade capacity validation to timetable diagnostics.
   - Verify: diagnostics report a specific input and generation error when a grade needs more than 40 slots.
2. Account for same-grade Cross groups in the capacity total.
   - Verify: a grade with 41 raw hours and one same-grade Cross overlap is not reported as over capacity.
3. Run targeted tests and WPF build.
   - Verify: relevant diagnostic tests pass and the WPF project builds.
4. Archive this plan after verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
