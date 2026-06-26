# Restore the Total Solver Time Limit

1. Restore the 120-second overall generation limit while keeping the per-solution limit unlimited. Apply the remaining total time to optimization phases as well as solution attempts.
2. Restore the overall time-limit WPF setting and timeout-specific messages without restoring a per-solution limit control. Verify the WPF declaration.
3. Run focused solver and ViewModel tests, including cancellation, then build WPF before archiving this plan.
