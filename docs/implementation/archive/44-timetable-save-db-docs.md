## Goal

Document the WPF timetable save flow and the SQLite table columns involved in saved timetable persistence.

## Steps

1. Inspect save entry points and repository persistence code.
   - Verify: Confirm `ManualEditViewModel.SaveTimetable`, `WorkspaceService.SaveTimetable`, and `SqliteRepository.UpsertSavedTimetable` call order.

2. Inspect SQLite schema columns for saved timetable data and snapshot-related data.
   - Verify: Confirm `SavedTimetables`, `SavedTimetableManualCrossLinks`, and snapshot source tables from `SqliteSchema`.

3. Write a Korean documentation page under `docs/`.
   - Verify: The document explains the runtime flow, saved data shape, and table/column meanings.

4. Archive this plan after verification.
   - Verify: This file is moved to `docs/implementation/archive/`.
