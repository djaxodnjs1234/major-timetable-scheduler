# Normalize WPF Source Files

1. [x] Map each `Codex.*` replacement file to its canonical project file and inspect existing differences.
   Verified: all seven production destinations had no pending user changes.
2. [x] Move the replacement implementations and tests into their canonical source/test locations, then remove the build replacement rules.
   Verified: no project references a `Codex.*` source file; all ten moved files preserve their previous content after line-ending normalization.
3. [x] Delete the obsolete replacement files and build/test the WPF projects.
   Verified: both test-project builds succeed with zero warnings/errors, and the focused replacement tests pass (28/28). Full suites retain environment/policy-dependent failures unrelated to this file relocation.
