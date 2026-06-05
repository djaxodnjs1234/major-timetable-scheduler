1. Study the Python prototype Cross manager.
   - Verify: capture its candidate rules, duplicate prevention, add/delete behavior, and solver data shape.
2. Remove the current per-course Cross manual setting.
   - Verify: no `GroupCrossPicker` or per-course Cross setting remains in the course row UI/code-behind.
3. Rebuild Cross setup in the auto-generation section.
   - Verify: the solve panel shows existing Cross groups, offers valid course-pair candidates, and supports add/delete before running the solver.
4. Keep solver/domain behavior intact.
   - Verify: `Workspace.CrossGroups` remains the single source passed to solver HC-16 and saved in snapshots/DB.
5. Add/update regression tests.
   - Verify: central Cross manager prevents duplicates/self-pairs, persists add/delete to workspace, and course rows no longer expose Cross editing.
6. Run checks and manual surface QA.
   - Verify: focused tests, full tests, isolated WPF build, and a small ViewModel/XAML driver all pass.
