# Course Block Room Consistency

Assumptions:
- A course without `FixedRooms` should still use one automatically selected room across all of its scheduled slots.
- Multi-room fixed courses keep the current behavior: every listed fixed room is occupied at the same scheduled slot.
- Explicit `FixedRooms` keep course-level priority and are not overridden by automatic room consistency.

Evidence:
- Saved timetable `course-room-mismatch` shows single-section auto-room courses with `BlockStructure=[1,2]` using different rooms between the 1-hour block and the 2-hour block.
- `BlockHcs.AddHc06_BlockSplit` currently creates `roomChoice` variables per block start candidate, so different blocks of the same course may legally choose different rooms.
- Before this change, HC-22 only applied when a base course had two or more sections. It did not cover a single course's own multiple blocks or fixed-time slots.

Plan:
1. Add a solver regression test for a single-section auto-room course split into multiple blocks.
   - Verify: forcing block one to `R1` and block two to `R2` is infeasible.
2. Add a solver regression test for a fixed-time course with no `FixedRooms`.
   - Verify: forcing two fixed slots of the same course to different rooms is infeasible.
3. Implement course-level automatic room consistency for every course with empty `FixedRooms`.
   - Verify: each such course selects one shared room and all `x[(course, day, period, room)]` assignments imply that room.
4. Preserve existing multi-section and explicit fixed-room behavior.
   - Verify: existing HC-14/HC-22 tests still pass, including multi-room fixed courses and mixed fixed-room section groups.
5. Update HC-22 wording in docs if the constraint name remains reused for this broader behavior.
   - Verify: WPF README and HC/SC docs describe "automatic course slots use one common room" rather than only "sections".
6. Run focused tests.
   - Verify: `dotnet test wpf/TimetableScheduler.Tests/TimetableScheduler.Tests.csproj --filter HcCoverageTests`.
