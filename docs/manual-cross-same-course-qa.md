# Manual Cross same-course final QA

## Purpose

Verify manual Cross behavior for classes that share the same `CourseId` but differ by `Section`, professor, and room.

## Scope

- Screen 4 manual edit.
- Hover `+` Cross badge.
- `+` click Cross creation.
- Drag/drop Cross creation.
- Save and reload.
- Cross label and sub-column display.
- Cross blocking rules.

## Implementation summary

- Manual Cross now uses an assignment-level key instead of `CourseId` alone.
- The key includes `CourseId`, `Section`, `Day`, `Period`, `RowSpan`, and normalized room IDs.
- Saved manual Cross rows use existing endpoint fields: course, section, day, period, and room.
- Restore resolves saved Cross links by saved endpoint data instead of the first matching `CourseId`.
- Cross display keys for `CrossParallelOrder` and `CrossLinkLabels` are assignment-level.
- `ConflictDetector` manual Cross exceptions apply only to the linked assignment pair's grade overlap.
- Hover, `+` click, and drag/drop Cross paths use assignment-level identity and do not block only because `CourseId` is the same.

## Manual QA checklist

1. Prepare two classes with the same `CourseId` but different section, professor, and room.
2. Hover the target class and confirm the `+` Cross badge appears.
3. Click the `+` badge and confirm the Cross is created.
4. Create the same type of Cross by drag/drop onto the Cross target.
5. Confirm the Cross classes are displayed in separate sub-columns.
6. Confirm each class has a Cross label tied to the assignment, not only `CourseId`.
7. Save the timetable.
8. Leave manual edit through the saved timetable list or timetable selection flow.
9. Reopen the saved timetable for editing.
10. Confirm the Cross relationship is restored on the same source/target endpoints.
11. Confirm Cross labels and sub-column ordering remain after reload.
12. Try Cross with the same professor or overlapping team-teaching professor and confirm it is blocked.
13. Try Cross with the same room and confirm it is blocked.
14. Try Cross between one-hour and two-hour blocks and confirm it is blocked.
15. Try Cross from an already Crossed assignment to a third assignment and confirm it is blocked.
16. Confirm existing normal Cross between different `CourseId` values still works.
17. Confirm normal Move still works.
18. Confirm normal Swap still works.
19. Confirm normal drag/drop move still works.

## Automated tests

- `ManualCrossAssignmentKey`
- `ManualCrossLink`
- `ManualCrossScenario`
- `ManualCrossQa`
- `ManualCross` ConflictDetector coverage
- `CrossParallelOrder`
- `CrossLinkLabels`

## Known limitation

`UnifiedTimetableViewModel.Render` can still be limited when the session contains multiple `Course` metadata objects with the exact same `Course.Id`. That render path maps course metadata by `Course.Id`, so it may not fully distinguish duplicated course metadata without a larger model/render contract change. This limitation is outside the final cleanup scope and is left unchanged.

## Existing failing test

- `RoomChange_WorkspaceOnlyRoom_SaveLoadRestoresRoomId`

This is an existing failure unrelated to the manual Cross same-course refactor and is intentionally left unchanged.
