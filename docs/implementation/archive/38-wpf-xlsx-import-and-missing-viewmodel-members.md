# 38-wpf-xlsx-import-and-missing-viewmodel-members

1. Inspect the XLSX import path and the failing test expectations.  
   Verify: identify the specific import error path and the missing `DataInputViewModel` members used by tests.

2. Patch the minimal production code needed to restore import behavior and test compatibility.  
   Verify: course handbook import code handles the failing case, and the missing ViewModel members compile.

3. Run solution build and tests.  
   Verify: confirm whether the previous compile failures are resolved and note any remaining issues.

4. Archive this plan file.  
   Verify: move the file into `docs/implementation/archive/`.
