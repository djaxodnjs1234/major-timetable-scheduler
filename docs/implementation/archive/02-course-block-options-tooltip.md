# Course Block Options And Tooltip

## Assumptions

- The requested block-structure choices apply to the course information management UI and its shared helper.
- Valid choices should be:
  - 2 hours: `2`
  - 3 hours: `1+2`, `3`
  - 4 hours: `2+2`, `4`
- The unavailable-room tooltip should be plain: selected rooms are not assigned to the course.
- The existing course edit save flow should be inspected before answering whether edits persist before pressing the completion button.

## Steps

1. Inspect the current save flow for course edits.
   - Verify: identify whether `완료` is required for persistence.

2. Update block-structure options.
   - Verify: focused ViewModel tests expect the new option lists.

3. Update the unavailable-room tooltip.
   - Verify: focused XAML test or source check expects the new text.

4. Run build/tests.
   - Verify: `dotnet build` and focused tests pass.

5. Archive this plan after completion.
   - Verify: move this file to `docs/implementation/archive/`.
