## Goal

Update the WPF course detail editor so changing weekly hours always resets block structure to that hour count's default shape and clears fixed-time state, with a UI-facing regression test for the fixed checkbox.

## Steps

1. Inspect the weekly-hours change path in the WPF course editor.
   - Verify: Confirm `DataInputView.xaml`, `DataInputView.xaml.cs`, and `DataInputViewModel` are the only touch points needed for the hours-change behavior.

2. Change the ViewModel hours handler to reset from the selected hours value.
   - Verify: A weekly-hours change sets `HoursPerWeek`, resets `BlockStructure` to the default option for that value, and clears `IsFixed` plus `FixedSlots`.

3. Refresh the WPF editor UI after weekly-hours changes.
   - Verify: The block-structure combo updates to the new default and the visible `시간 고정` checkbox becomes unchecked.

4. Add regression coverage.
   - Verify: Tests cover both the ViewModel reset behavior and the WPF view-level unchecked-checkbox behavior.

5. Run available validation and archive the plan.
   - Verify: Relevant tests/builds pass where the environment supports them, then move this file to `docs/implementation/archive/`.
