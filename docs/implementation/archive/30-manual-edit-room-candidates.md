# Manual Edit Room Candidate Change

1. Inspect the existing room-change flow in `ManualEditViewModel`.
   - Verify: identify the selected assignment room source, room candidate policy, and commit/conflict path.
2. Restrict the Inspector room selector to valid room candidates for the selected assignment.
   - Verify: single-candidate selections expose display text only; multiple candidates expose the ComboBox.
3. Validate room changes before committing.
   - Verify: invalid candidates or room-conflict changes leave the working assignment unchanged and do not create undo snapshots.
4. Add focused ViewModel/XAML tests where practical.
   - Verify: room candidates, valid change, conflict rejection, and undo behavior are covered.
5. Run the requested build and test commands, then archive this plan.
   - Verify: WPF project builds and test project completes.
