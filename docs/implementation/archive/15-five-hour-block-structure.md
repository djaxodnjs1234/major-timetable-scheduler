# Five Hour Block Structure

## Goal
Support five-hour courses in WPF with block structure options `1+2+2` and `2+3`.

## Assumptions
- Only WPF code is in scope.
- Five-hour courses should use `1+2+2` as the default option, matching the first UI option pattern.
- Existing arbitrary-hour fallback remains unchanged for hours other than 1-5.

## Steps
1. Add `5` to course hour choices and block structure options.
   - Verify: ViewModel tests cover hour option and generated block options.
2. Allow the same five-hour structures in diagnostics and Excel import defaults.
   - Verify: tests cover valid five-hour diagnostics and loader defaults.
3. Run targeted tests and WPF build.
   - Verify: relevant tests pass and WPF compiles.
4. Archive this plan.
   - Verify: this file is moved to `docs/implementation/archive/`.
