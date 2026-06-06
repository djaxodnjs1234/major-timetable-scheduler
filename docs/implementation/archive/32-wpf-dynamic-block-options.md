1. Inspect the current Course Information weekly-hours and block-structure edit flow.
   - Verify: confirm the exact ViewModel, converter, and XAML event hooks used by `HoursPerWeek`, `BlockStructure`, and fixed-slot editing.
2. Add focused regression tests for dynamic block-option generation.
   - Verify: tests cover 2-hour, 3-hour, and 4-hour option lists and confirm `+` display text.
3. Implement weekly-hours-based block option generation in the ViewModel.
   - Verify: 2시간 shows `1+1`, `2`; 3시간 shows `1+2`, `2+1`, `3`; 4시간 shows `2+2`, `3+1`, `1+3`, `4`.
4. Reset fixed-time state when block structure changes.
   - Verify: changing the effective block structure clears `IsFixed` and `FixedSlots` for the affected course sections.
5. Wire the course edit UI to refresh block options immediately.
   - Verify: changing weekly hours refreshes the dropdown options, coerces invalid selections to a valid default, and rebuilds the fixed-slot editor.
6. Run build verification and archive this plan.
   - Verify: `dotnet build wpf/TimetableScheduler.slnx` succeeds, then move this file to `docs/implementation/archive/`.
