# Professor First-Period Soft Constraint

## Goal

Add SC04: penalize each professor's non-fixed first-period classes above two
per week.

## Steps

1. Add SC04 constants and solver penalty term.
   - Verify: the term counts non-fixed first-period teaching days per professor
     and excludes fixed courses.
2. Add SC04 to the lexicographic solver flow.
   - Verify: `DiverseSolverOptions`, `DiverseSolverResult`, phase bounds, and
     Phase 2 constraints all carry SC04.
3. Add SC04 to scoring and normalized result cards.
   - Verify: `SolutionScore` includes `Sc04`, total score includes the new
     weight, and result card normalization uses four soft constraints.
4. Expose SC04 in the generation options UI.
   - Verify: `DataInputViewModel` passes `UseSc04` into solver options.
5. Update and add focused tests.
   - Verify: scoring tests cover the threshold and fixed-course exclusion, and
     solver tests cover the bound when SC04 is enabled.
6. Run targeted tests and archive this plan.
   - Verify: build/tests either pass or any unrelated blocker is documented.
