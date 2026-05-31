# Cross Candidate Range Fix and Inspector Minimal Polish

## Goal
Fix Cross creation so a selected course can move to the target course time when durations match, and minimally polish the manual edit inspector panel.

## Steps
1. Adjust Cross range validation.
   - Verify: different-time same-duration Cross creates ManualCrossLink and moves selected course to target time.
2. Keep Cross duration mismatch blocked.
   - Verify: 1h/2h Cross does not mutate state or undo stack.
3. Apply small ManualEditView XAML padding/header/empty-state polish only.
   - Verify: WPF build succeeds.
4. Run requested build and test commands.
   - Verify: both pass.
