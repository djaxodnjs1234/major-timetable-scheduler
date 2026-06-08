# WPF Readable Tooltips

1. Find long WPF tooltips.
   - Verify: locate all ToolTip usages in WPF XAML and identify long inline text.

2. Add line breaks to long tooltip text.
   - Verify: long explanations use explicit new lines while short tooltips remain unchanged.

3. Preserve UI behavior.
   - Verify: only tooltip presentation changes; bindings and controls are untouched.

4. Run XAML validation.
   - Verify: WPF project builds successfully or unrelated environment failures are documented.

5. Archive this plan.
   - Verify: move this plan to docs/implementation/archive/ when done.
