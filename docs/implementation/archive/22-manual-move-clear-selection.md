# Manual move clear selection restore

1. Inspect selection state and move success/failure paths.
   - Verify: identify the existing clear-selection function and the exact click/DND move paths.
2. Restore selection clearing only after successful moves.
   - Verify: click move and DND move success clear selection; failed validation leaves selection unchanged.
3. Add focused ManualEditViewModel tests.
   - Verify: success/failure selection state and edit state clearing are covered without changing Cross, Swap, ForceMove, or validation policies.
4. Run required build and tests, then archive this plan.
   - Verify: requested dotnet build and dotnet test commands pass, and this plan is moved to archive.
