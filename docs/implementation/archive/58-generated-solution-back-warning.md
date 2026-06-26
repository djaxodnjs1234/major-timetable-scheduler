## Goal

Warn users when navigating back from the data input page would discard generated timetable solutions, and inspect the manual-edit navigation flow for regressions.

## Plan

1. Trace data-input back navigation and generated-solution state.
   - Verify: identify the exact condition that means generated solutions would be lost.
2. Trace manual-edit entry and back-target handling from selection, results, and data input.
   - Verify: document any concrete flow issue found in code or tests.
3. Implement a minimal warning condition for generated solutions on data-input Back.
   - Verify: add/update focused ViewModel tests.
4. Run targeted tests and WPF build.
   - Verify: `dotnet test` and `dotnet build` pass for the touched area.
5. Archive this plan after verification.
