# OR-Tools Internal Usage Explanation

1. Add a plain-Korean explanation of OR-Tools CP-SAT internal behavior to the program/solver guide.
   - Verify: the section explains variables, constraints, propagation, search, objective, and why OR-Tools does not understand timetables by itself.
2. Explain exactly how this project uses OR-Tools.
   - Verify: the section maps courses/days/periods/rooms to `x`, `y`, block start variables, hard rules, soft preferences, and result extraction.
3. Verify readability and archive this plan.
   - Verify: search finds the new OR-Tools section and this plan is archived.
