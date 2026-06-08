1. Create localized WPF UI plan
   - verify: plan file exists in `docs/implementation/`

2. Inspect Data Input XAML and edit-state bindings
   - verify: identify only the files needed for Course, Professor, and Classroom sections

3. Unify Course, Professor, and Classroom information UI
   - verify: edit/complete button placement and read-only versus edit-mode visuals match the requested pattern

4. Adjust professor unavailable-time matrix visuals
   - verify: selected cells render a light-gray full-cell X while headers remain blue

5. Build and test the WPF solution
   - verify: `dotnet build wpf/TimetableScheduler.slnx` succeeds and run `dotnet test wpf/TimetableScheduler.slnx` if tests exist

6. Archive the implementation plan
   - verify: move the plan file into `docs/implementation/archive/` after implementation and verification
