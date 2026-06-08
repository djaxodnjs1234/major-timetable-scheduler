1. Remove ViewModel selection changes from drag start and DragOver.
   - Verify: drag start does not call `CellClicked`, and DragOver does not trigger `SetEditState` or `Rebuild`.
2. Add source/target-based Cross/Swap hover evaluation for DND and use it from the WPF control.
   - Verify: existing Cross/Swap rules are reused without changing move, HC-20, or overlap policies.
3. Cache drag hover badge state in the control so repeated DragOver does not recreate visual elements or flicker effects.
   - Verify: same target and badge state returns without rebuilding badge visuals.
4. Run the requested WPF build and test commands, then archive this plan.
   - Verify: both commands pass.
