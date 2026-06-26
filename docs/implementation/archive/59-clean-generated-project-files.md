# Clean Generated Project Files

1. Keep tool session data, verification artifacts, and the local Codex connection setting out of version control.  
   Verify: `.gitignore` covers only local/generated files and leaves shared implementation archives trackable.
2. Remove the existing local WPF `bin` and `obj` build outputs.  
   Verify: no `bin` or `obj` directories remain below `wpf`.
3. Confirm the staged repository changes consist of the intended cleanup and no source files were removed.  
   Verify: inspect `git status` and the deletion summary.
