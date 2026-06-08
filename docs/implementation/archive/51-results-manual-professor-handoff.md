## Goal

Fix missing professor information when navigating from a selected solution in results preview to manual edit by making the handoff use the exact snapshot behind the rendered results.

## Plan

1. Inspect the results-to-manual navigation and snapshot source used by each side.
   - Verify: identify whether manual edit reloads from a different data source than results preview.

2. Patch results handoff to always pass a concrete snapshot to manual edit.
   - Verify: manual edit uses the same course/professor/room metadata that results preview rendered.

3. Add regression coverage for live-workspace and session-backed results navigation.
   - Verify: professor information remains present after `해 미리보기 -> 수동 편집`.

4. Build, run focused tests, and exercise the flow through a driver.
   - Verify: results preview and manual edit show matching professor information.
