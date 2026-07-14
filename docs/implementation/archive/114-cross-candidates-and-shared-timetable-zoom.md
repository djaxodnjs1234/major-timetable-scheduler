# Cross candidates and shared timetable zoom

## Assumptions

- "Cross manual setting" means the Cross candidate list in the Data Input screen.
- "Empty cell item" means a candidate entry whose course display/base identity is blank or effectively empty; it should not appear as a selectable Cross candidate.
- Timetable zoom should be shared across timetable-related WPF screens in the same app session.

## Plan

1. Inspect Cross candidate creation and add a filter for blank/empty course entries.
   - Verify: candidate list tests cover blank entries not appearing.
2. Replace per-view timetable zoom instances with one shared zoom instance.
   - Verify: changing zoom from one timetable screen affects the other screens' zoom binding source.
3. Add/adjust focused tests where practical.
   - Verify: relevant ViewModel tests and build pass.
4. Run focused tests and WPF build, then archive this plan.
   - Verify: commands complete successfully.

## Notes for answer

- Explain "time-band violation" as the academic-level time-band rule.
- Explain conflict display scopes for timetable labels, right-side constraint panel, and full validation.
