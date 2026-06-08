# 37-wpf-text-repair-and-xlsx-import-fix

1. Revert corrupted text-prone files to the last committed state.  
   Verify: affected WPF/ViewModel files no longer contain newly introduced broken string literals.

2. Reapply only the requested course handbook import fix with minimal edits.  
   Verify: explicit section codes like `GA1004-01`, `GA1004-02` load as separate sections except when the course name contains `캡스톤디자인`.

3. Run targeted build/test commands.  
   Verify: WPF/ViewModel projects build; note any pre-existing solution test failures separately.

4. Archive this plan file.  
   Verify: file moved to `docs/implementation/archive/`.
