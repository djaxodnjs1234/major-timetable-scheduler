# Two-line professor and comma-separated room

Assumption: class-card title remains two lines, professor names should also allow two lines before ellipsis, and rooms should render as one comma-separated line with ellipsis only when that line overflows.

1. Update class-card metadata layout.
   - Verify: professor text wraps within a two-line height budget, room text stays one line.
2. Change room display formatting.
   - Verify: room labels split by existing line breaks and rejoin with `, `.
3. Update focused tests.
   - Verify: tests cover professor two-line budget and comma-separated room formatting.
4. Run focused WPF tests and build with an unlocked output directory.
   - Verify: report exact commands and results.
