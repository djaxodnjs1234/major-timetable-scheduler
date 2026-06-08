1. Inspect the WPF Course Information row template and display helpers before editing.
   - Verify: `DataInputView.xaml`, `DataInputViewModel.cs`, and the block-list converter are the only required touch points for this UI-only pass.
2. Add focused ViewModel regression coverage for course summary display formatting.
   - Verify: tests cover `+` block text and blank course summary values instead of `-`.
3. Update course display formatting helpers without changing solver or splitting behavior.
   - Verify: Course Information block text uses `+`, and empty course-summary cells render as blank strings.
4. Convert the course editor to row-specific read-only text before edit mode.
   - Verify: before `수정`, course fields show plain text; after `수정`, the existing ComboBox and CheckBox-based editors appear.
5. Polish the Course Information visuals with targeted XAML changes.
   - Verify: the course header row is blue with white text, the text to the right of the course `수정` button is removed, and the course-row area reads as one continuous light-gray block without white gaps.
6. Run WPF build verification and archive this plan.
   - Verify: `dotnet build wpf/TimetableScheduler.slnx` succeeds, then move this file to `docs/implementation/archive/`.
