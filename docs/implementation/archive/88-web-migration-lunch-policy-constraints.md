# Web Migration Constraint Policy Document

1. Draft the web migration reference document for information input, hard constraints, soft constraints, solver phases, and diagnostics.
   - Verify: the document includes all current WPF solver constraints and the input fields needed to configure them.
2. Design the configurable lunch policy with the modes `BAN_4`, `BAN_5`, and `BAN_AT_LEAST_ONE_OF_4_5`.
   - Verify: every affected rule describes how to derive allowed periods, block starts, fixed-slot validation, conflict detection, manual editing, and rendering from the policy.
3. Review the document for implementability and move this plan to `docs/implementation/archive/`.
   - Verify: search confirms the new migration document contains the lunch policy modes and this plan file is archived.
