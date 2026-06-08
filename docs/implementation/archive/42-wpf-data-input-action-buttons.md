## Goal

Unify the action buttons in WPF data input management rows for courses, professors, and rooms.
Edit and delete stay at the bottom, and pressing edit replaces that same button slot with a done button.

## Steps

1. Inspect current WPF data input row templates and edit-state commands.
   - Verify: Confirm the affected files and the current `IsEditing`-driven behavior in `DataInputView.xaml`, `DataInputView.xaml.cs`, and `DataInputViewModel.cs`.

2. Add or extend focused ViewModel tests for edit/save state transitions.
   - Verify: `EditProfessorCommand` / `SaveProfessorCommand` and `EditRoomCommand` / `SaveRoomCommand` toggle `IsEditing` as expected, while existing course save coverage still passes.

3. Update WPF row templates to use one bottom action area.
   - Verify: In `DataInputView.xaml`, course, professor, and room rows all show bottom action buttons; `수정` is visible only when not editing; `완료` is visible only while editing; delete remains bottom-aligned.

4. Run WPF validation.
   - Verify: Relevant diagnostics are clean and `dotnet test wpf/TimetableScheduler.Tests` passes.

5. Archive this plan after the change is verified.
   - Verify: This file is moved to `docs/implementation/archive/`.
