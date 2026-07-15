# Validation check count wording

## Assumptions

- Red means a blocking error; yellow means a warning that should be visible but is not treated as a hard blocker in manual edit.
- The full-validation dialog should not show separate `Error N / Warning N` text per row anymore.
- Each full-validation row should show only how many violations exist for that item, with error count prioritized as the user requested.

## Plan

1. Inspect validation check row data and WPF dialog rendering.
   - Verify: identify the single text property or row builder that shows `Error N건, Warning N건`.
2. Change the validation check summary text to remove explicit Warning count wording.
   - Verify: add/update tests around `ValidationCheckItem.Summary` or source-level dialog output.
3. Confirm right-panel red/yellow meanings from severity classification and list current warning items.
   - Verify: cite the relevant classification code in final response.
4. Run focused tests and a solution build, then archive this plan.
   - Verify: report exact commands and results.
