# Grade Color Legend

1. Inspect timetable rendering controls and grade color source.
   - Verify: identify all timetable controls and confirm block colors use `GradeToBrushConverter.BrushFor`.
2. Add a compact grade legend to the common timetable controls.
   - Verify: unified and tabbed timetable controls show the same 1-4 grade legend without changing grid row/column logic.
3. Reuse the existing grade color map.
   - Verify: legend swatches bind through `GradeToBrushConverter` or call the same `BrushFor` map.
4. Add focused converter tests where possible.
   - Verify: numeric and string grade values resolve to the same brush.
5. Run the requested build/test commands, then archive this plan.
   - Verify: WPF build and full test suite pass.
