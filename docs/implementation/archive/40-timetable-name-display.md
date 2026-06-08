# 40-timetable-name-display

1. Inspect timetable cell display paths for WPF and Python prototype.  
   Verify: identify where professor and room ids are converted to visible cell text.

2. Add name lookup for WPF timetable cells.  
   Verify: result/preview and manual-edit timetable cells show professor and room names, falling back to ids when no name exists.

3. Add the same name lookup for the Python prototype renderer.  
   Verify: prototype timetable cells show professor and room names, falling back to ids when no name exists.

4. Run build/tests or focused drivers for changed display logic.  
   Verify: changed files are diagnostic-clean and the visible cell text contains names, not raw ids.

5. Archive this plan file.  
   Verify: move this file into `docs/implementation/archive/` after verification.
