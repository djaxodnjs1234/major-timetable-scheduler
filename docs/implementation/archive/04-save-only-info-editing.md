# Save-Only Info Editing

## Assumptions

- Information-input edits should not mutate the workspace until the user presses `완료`.
- The main reported problem is in course information management, including checkbox fields such as `시간 고정`.
- Professor and room rows use the same direct-object pattern, so they should follow the same save-only behavior if touched by the same edit flow.
- Existing validation and persistence should remain in the existing `Save*` commands.

## Steps

1. Inspect current edit bindings and save commands.
   - Verify: identify where row edit state currently points at workspace objects.

2. Add edit buffers for rows.
   - Verify: changing fields while editing mutates only the buffer, not workspace data.

3. Save from buffers on `완료`, including unchecked checkbox values.
   - Verify: unchecking a checkbox and pressing `완료` updates the workspace.

4. Add focused regression tests.
   - Verify: tests cover no pre-save mutation and checkbox uncheck save.

5. Run build/tests and archive this plan.
   - Verify: `dotnet build` and relevant tests pass, then move this file to `archive`.
