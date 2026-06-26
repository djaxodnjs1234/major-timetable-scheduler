# Delete Confirmation Prompts

## Goal
Ask for confirmation before deleting items from the data input screen.

## Scope
- Course groups and fixed individual course sections.
- Professors.
- Rooms.
- Cross groups.

## Assumptions
- Delete confirmation is a UI concern, so it belongs in `DataInputView.xaml.cs` rather than the UI-agnostic ViewModel.
- Existing ViewModel delete commands should remain unchanged and run only after the user confirms.

## Steps
1. Replace direct delete command bindings in `DataInputView.xaml` with click handlers where needed.
   - Verify: professor, room, and Cross delete buttons route through code-behind.
2. Add confirmation prompts in `DataInputView.xaml.cs`.
   - Verify: every delete handler returns without deleting unless the user selects Yes.
3. Build the WPF project.
   - Verify: XAML compiles.
4. Archive this plan after verification.
   - Verify: this file is moved to `docs/implementation/archive/`.
