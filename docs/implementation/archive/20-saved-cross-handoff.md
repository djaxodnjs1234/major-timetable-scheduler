# Saved Cross handoff into manual edit

1. Trace the existing saved-timetable edit handoff.
   - Verify: identify where assignments are carried and manual cross links are dropped.
2. Add manual cross links to the existing edit handoff.
   - Verify: DataInputViewModel stores saved links, exposes them with the handoff, and MainWindowViewModel passes them to ManualEditViewModel.
3. Restore manual cross links before the first rerender in the snapshot load path.
   - Verify: LoadFromSnapshot can restore saved links before RefreshConflicts and starts saved-cross suppression only when saved links were supplied.
4. Add focused tests for the real handoff path and policy preservation.
   - Verify: tests cover link delivery, no HC-11 after rerender, suppression persistence, suppression release after edit, and HC-20 preservation.
5. Run build and tests, then archive this plan.
   - Verify: dotnet build and dotnet test complete successfully, and this file is moved to docs/implementation/archive/.
