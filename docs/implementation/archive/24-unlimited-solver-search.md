# Unlimited Solver Search

1. Treat zero time limits as unlimited in the solver for both optimization phases and per-solution search attempts. Verify that an unlimited option does not add a CP-SAT time limit.
2. Configure WPF timetable generation to use unlimited search by default and remove the time-limit controls and timeout-specific guidance. Verify the WPF declarations reflect the new behavior.
3. Run the focused solver and ViewModel tests, verify cancellation still works, and build WPF before archiving this plan.
