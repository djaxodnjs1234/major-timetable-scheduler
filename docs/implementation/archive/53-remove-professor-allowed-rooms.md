# Remove Professor Allowed Rooms

## Assumptions

- Professor allowed rooms are no longer an active WPF feature.
- Remove the confusing `AllowedRooms` code path, while keeping professor unavailable rooms and course fixed/unavailable room behavior unchanged.
- Existing SQLite databases may still contain an old `AllowedRoomsJson` column; the app should ignore it rather than depend on it.

## Steps

1. Remove `AllowedRooms` from the domain model and clone/persistence mappings.
   - Verify: no production C# code reads or writes `Professor.AllowedRooms`.

2. Remove allowed-room solver and diagnostic behavior.
   - Verify: HC-21 and diagnostics only consider professor unavailable rooms and course room settings.

3. Remove allowed-room UI display and tests.
   - Verify: WPF manual edit no longer shows professor allowed rooms, and tests compile.

4. Update docs to remove professor allowed-room wording.
   - Verify: docs no longer describe `AllowedRooms` as a supported setting.

5. Run focused tests/build and archive this plan.
   - Verify: relevant tests and WPF build pass, then move this file to `archive/`.
