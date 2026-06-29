# Remove class-card ellipsis

Assumption: only the ellipsis behavior from the previous class-card readability change should be removed. The larger course title, smaller professor/room text, and readable line breaks should remain.

1. Remove ellipsis trimming from WPF class-card text blocks.
   - Verify: course, professor, and room text use wrapping with no `CharacterEllipsis`.
2. Update focused source-level regression coverage.
   - Verify: tests still check the typography sizes and confirm ellipsis is absent.
3. Run focused WPF tests and build.
   - Verify: report the exact commands and results.
