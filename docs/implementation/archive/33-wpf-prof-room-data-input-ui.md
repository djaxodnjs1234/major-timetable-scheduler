1. Inspect the current professor and classroom Data Input row templates and unavailable-time rendering.
   - Verify: identify the exact XAML/control files for professor rows, room rows, and time-cell visuals.
2. Add focused regression coverage for row edit-state toggling where possible.
   - Verify: tests cover professor and room `IsEditing` transitions through existing commands.
3. Update professor UI without touching Course Information.
   - Verify: professor name is read-only, and selected unavailable-time cells use a softer hatched/diagonal visual instead of `X`.
4. Update classroom UI without touching Course Information.
   - Verify: classroom name is read-only, read-only mode shows plain text, edit mode shows controls, and the primary button swaps Edit/Save in the same position while the button group sits bottom-left.
5. Run build verification and archive this plan.
   - Verify: `dotnet build wpf/TimetableScheduler.slnx` succeeds, then move this file to `docs/implementation/archive/`.
