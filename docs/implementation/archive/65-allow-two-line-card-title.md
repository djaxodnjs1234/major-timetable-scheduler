# Allow two-line class-card title

Assumption: only the class-card title should be allowed to wrap to two lines. Professor and room text should keep the current overflow-preserving behavior.

1. Update the WPF class-card title layout.
   - Verify: title text wraps up to two title line-heights and still trims with ellipsis when it exceeds that space.
2. Update focused source-level coverage.
   - Verify: tests check the title two-line budget and existing typography settings.
3. Run focused WPF tests and build using an unlocked output directory if the running app has locked `bin`.
   - Verify: report exact commands and results.
