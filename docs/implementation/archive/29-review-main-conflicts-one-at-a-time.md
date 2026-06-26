# Review Main Changes One at a Time

1. Compare each previously conflicted file against the merge base and present a minimal resolution proposal.
   Verify: no source changes are made before user approval.
2. Apply only an approved proposal, then test that file's affected behavior.
   Verify: user-approved change and focused tests pass.
3. Repeat until every requested conflict is reviewed, then archive this plan.
   Verify: all approved resolutions are tested and the merge outcome is reviewed.
