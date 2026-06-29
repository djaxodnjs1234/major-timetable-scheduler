# Overflow-only class-card ellipsis

Assumption: class-card text should normally stay readable with separate title/professor/room lines, but any one field that would make a timetable cell grow should be constrained and show ellipsis only when it overflows.

1. Constrain each class-card text field independently.
   - Verify: title, professor, and room lines use `CharacterEllipsis` with fixed line budgets, while typography sizes remain unchanged.
2. Preserve readable room line breaks without letting many rooms grow the card.
   - Verify: room labels keep line breaks and extra room lines collapse into an ellipsis marker.
3. Update focused tests for the overflow policy.
   - Verify: tests check typography, per-field trimming settings, and room-line compaction.
4. Run focused WPF tests and build using an unlocked output directory if the running app has locked `bin`.
   - Verify: report the exact commands and results.
