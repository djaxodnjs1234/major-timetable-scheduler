# Night separator as line

Assumption: the current WPF timetable already inserts a separate night-class separator before period 10, and the requested change is to render that separator as a thick line only, not as a table row with visible height or label-like space.

1. Inspect the unified and standard timetable controls plus their night-layout tests.
   - Verify: identify where the separator row is inserted and how its height/border is styled.
2. Update the WPF timetable separator rendering to use only a thick line before night periods.
   - Verify: lunch remains a one-hour row, while the night separator does not consume slot-like vertical space.
3. Adjust or add focused tests for the separator layout.
   - Verify: existing WPF layout tests pass and cover the changed separator behavior.
4. Run the relevant WPF test suite.
   - Verify: report the exact test command and result.
