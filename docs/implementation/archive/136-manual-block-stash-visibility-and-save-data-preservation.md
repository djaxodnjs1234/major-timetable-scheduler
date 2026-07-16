1. Reproduce the two reported manual-edit issues in code paths.
   - Verify: identify why staged blocks become hidden on short screens and why saved course data still disappears after adding a block.
2. Make the block stash usable on shorter windows.
   - Verify: the staged block list keeps visible space and can scroll independently.
3. Fix manual-block save data preservation across the saved timetable reload path.
   - Verify: a saved timetable created after adding a manual block reloads into course management with original course details intact.
4. Add focused regression tests for both fixes.
   - Verify: tests fail before the fix or cover the exact broken paths.
5. Run targeted and relevant broader tests, then archive this plan.
   - Verify: record any remaining unrelated failures separately.
