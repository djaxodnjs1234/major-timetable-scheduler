## Goal

Improve WPF course operating-info display formatting and block fixed-time overlap saves with a warning before persistence.

## Steps

1. Inspect course read-only operating-info bindings and course save path.
   - Verify: Confirm `DataInputView.xaml`, `DataInputView.xaml.cs`, `DataInputViewModel.cs`, and `FixedSlotEditorViewModel.cs` are the relevant files.

2. Update course operating-info display.
   - Verify: Each read-only operating-info item renders on its own line, with `불가강의실: 없음` / `팀티칭 교수: 없음` fallback formatting.

3. Add fixed-time overlap detection before save.
   - Verify: Saving fixed slots is blocked when another fixed course already occupies the same day/period, and a warning popup is shown instead of any raw exception.

4. Run focused tests and WPF build.
   - Verify: `CourseGroupsTests` passes, full test suite passes, and WPF project builds with local dotnet.

5. Archive this plan after verification.
   - Verify: This file is moved to `docs/implementation/archive/`.
