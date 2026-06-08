# 39-wpf-xlsx-import-auto-increment-course-ids

1. Inspect the WPF XLSX import path and confirm where imported course IDs are assigned.  
   Verify: identify a minimal change that makes imported course numbers start at 1 without breaking section grouping.

2. Patch the XLSX loader to assign sequential imported base IDs and preserve explicit section suffixes.  
   Verify: grouped courses import as `1`, `2`, ... and explicit sections import as `1-01`, `1-02`, ... for the same base course.

3. Update focused tests for the new imported ID behavior.  
   Verify: tests assert sequential numbering and section preservation instead of handbook source codes.

4. Run diagnostics and targeted test/manual import verification.  
   Verify: changed files are clean and the loader imports handbook data with sequential course numbering.

5. Archive this plan file.  
   Verify: move the file into `docs/implementation/archive/` after the task is complete.
