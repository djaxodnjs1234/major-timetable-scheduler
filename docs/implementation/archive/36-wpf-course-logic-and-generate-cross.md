1. Review course import and generate screen scope
   - verify: identify only the WPF files tied to section loading, block structure, fixed time reset, cross settings, and generate buttons

2. Fix course section import and block rules
   - verify: source section values are preserved and weekly-hours changes immediately refresh valid block options and defaults

3. Update generate cross behavior and layout
   - verify: cross groups start collapsed, incompatible options disable safely, and generate buttons move to the top without binding changes

4. Set generation time limit default
   - verify: the active default generation limit is 300 seconds

5. Run build and tests
   - verify: run `dotnet build wpf/TimetableScheduler.slnx` and `dotnet test wpf/TimetableScheduler.slnx`

6. Archive the plan file
   - verify: move this file to `docs/implementation/archive/`
