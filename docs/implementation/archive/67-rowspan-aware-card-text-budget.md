# RowSpan-aware class-card text budget

Assumption: one-period class cards should keep the compact overflow rules, while multi-period cards should use their extra vertical space to show more information before applying ellipsis.

1. Add RowSpan-based line budgets for class-card title, professor, and room text.
   - Verify: a one-period card keeps the current compact budgets, while a two-period card doubles the available line budget.
2. Apply those budgets in the WPF class-card renderer.
   - Verify: room labels remain comma-separated, but multi-period cards may wrap the comma-separated room line within their larger budget.
3. Update focused tests for the line-budget policy.
   - Verify: tests cover one-period and two-period budgets plus comma-separated rooms.
4. Run focused WPF tests and build with an unlocked output directory.
   - Verify: report exact commands and results.
