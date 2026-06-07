1. Add ViewModel booleans for selected blocking and warning reason visibility.
   - Verify: fixed selected courses expose a blocking reason, warning slots expose a warning reason, and normal selections expose neither.
2. Bind the Inspector reason rows to those booleans.
   - Verify: empty "none" reason rows are collapsed while the existing no-selection panel stays unchanged.
3. Run the requested WPF build and test commands, then archive this plan.
   - Verify: both commands pass.
