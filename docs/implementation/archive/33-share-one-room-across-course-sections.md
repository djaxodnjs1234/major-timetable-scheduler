# Share one room across automatically assigned course sections

1. Add a solver constraint that selects one common room for all automatically assigned sections of the same course base ID.
   - Verify: every generated section of the same course uses the same room across its scheduled slots.
2. Preserve explicit fixed-room precedence.
   - Verify: a course group containing explicit fixed rooms is not overridden by the automatic common-room constraint.
3. Add solver regression tests and run the focused WPF test project.
   - Verify: a forced different-room assignment is infeasible and the related test suite passes.
