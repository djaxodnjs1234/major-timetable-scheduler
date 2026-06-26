# IE-004 Missing Professor Status Display

## Goal
When timetable generation is blocked by IE-004, the status/progress message must show which course has no assigned professor.

## Assumptions
- `TimetableDiagnostics` already creates the IE-004 diagnostic with the course label.
- The status message only shows the first few diagnostics, so IE-004 can be hidden when other input errors exist first.
- The requested UI surface is `DataInputViewModel.StatusMessage`, which drives the progress/status text.

## Steps
1. Prioritize IE-004 diagnostics before truncating the status message.
   - Verify: IE-004 appears in `StatusMessage` even when there are more than five input errors.
2. Add a regression test for the status message.
   - Verify: the test checks both `IE-004` and the affected course name.
3. Run the targeted ViewModel test.
   - Verify: the new test and nearby input validation tests pass.
4. Archive this plan after verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
