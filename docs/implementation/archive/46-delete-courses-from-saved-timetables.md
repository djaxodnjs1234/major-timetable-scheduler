# Delete courses from saved timetable edits

1. Add an atomic session-only course deletion operation that removes invalid dependent scheduling constraints.
   - Verify: removing course sections cleans only Cross and retake rules made invalid by the removal.
2. Track deletion impact and prune the existing schedule handoff before entering manual editing.
   - Verify: deleted course assignments and manual Cross links are absent while unrelated assignments remain.
3. Show the destructive impact in the course deletion confirmation dialog.
   - Verify: cancelling makes no changes; confirming deletes exactly the previewed session data.
4. Add focused view-model/service tests, build the WPF project, and archive this plan.
   - Verify: relevant tests and WPF build pass.
