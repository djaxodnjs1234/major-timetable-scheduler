# Hide invalid Cross candidate grade groups

## Assumptions

- The blank Cross manual setting header comes from courses whose grade is not one of the supported display grades.
- Cross manual candidates should only show 1st, 2nd, 3rd, 4th, and graduate groups.

## Plan

1. Filter Cross candidate course groups to supported academic levels only.
   - Verify: a grade 0 or otherwise unsupported course does not create a blank Cross candidate group.
2. Add a focused ViewModel test.
   - Verify: candidate groups have no blank headers and invalid-grade candidates are absent.
3. Run the focused Cross manager tests and WPF build, then archive this plan.
   - Verify: commands complete successfully.
