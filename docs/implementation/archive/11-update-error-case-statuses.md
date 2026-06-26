# Update Error Case Statuses

## Goal
Update `docs/info_input_and_generation_error_cases.md` status values to match the currently implemented input and generation diagnostics.

## Status Rules
- `현재 처리`: the documented case is handled in the relevant save, pre-generation, or failure-diagnostic path.
- `현재 일부 처리`: part of the documented path is handled, but another listed detection point still needs implementation.
- `추가 필요`: no current implementation was found for the documented case.

## Steps
1. Compare the document IDs with `TimetableDiagnostics` and `DataInputViewModel` validations.
   - Verify: every updated status maps to current code.
2. Update only the status column and detection point wording where needed.
   - Verify: no IDs or test-purpose text are changed unnecessarily.
3. Check the diff and archive this plan.
   - Verify: the plan file is moved to `docs/implementation/archive/`.
