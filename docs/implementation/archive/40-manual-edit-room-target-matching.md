1. Strengthen room-change target matching -> verify: selected period inside a multi-hour block still updates the full selected block.
2. Add changed-zero diagnostics -> verify: failure message includes selected room-change state and candidate counts.
3. Preserve single and multi room change semantics -> verify: single uses NewRoomId, multi changes only SelectedOldRoomId.
4. Run build and tests -> verify: WPF project builds and test project passes.
