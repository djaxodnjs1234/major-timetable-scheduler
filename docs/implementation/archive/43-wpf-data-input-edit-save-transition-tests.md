1. Add focused ViewModel tests in `CourseGroupsTests.cs` for professor edit/save transitions.
   - Verify `EditProfessorCommand` sets `IsEditing = true`.
   - Verify `SaveProfessorCommand` persists and then clears `IsEditing`.
2. Add focused ViewModel tests in `CourseGroupsTests.cs` for room edit/save transitions.
   - Verify `EditRoomCommand` sets `IsEditing = true`.
   - Verify `SaveRoomCommand` persists and then clears `IsEditing`.
3. Run the targeted WPF test project.
   - Verify the new tests pass without changing production code.
