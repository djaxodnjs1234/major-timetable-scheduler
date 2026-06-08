1. Review WPF xlsx import behavior
   - verify: identify the loader and UI files for course sections, room shading, and name display

2. Update xlsx course section handling
   - verify: explicit ids like `GA1004-01` and `GA1004-02` stay as separate sections unless the course name contains `캡스톤디자인`

3. Fix imported room shading
   - verify: all rooms loaded from the xlsx path are marked as imported in the room management list

4. Adjust professor and room name display
   - verify: the detail panels show plain text in the `이름: 홍길동` style

5. Run build and tests
   - verify: run `dotnet build wpf/TimetableScheduler.slnx` and `dotnet test wpf/TimetableScheduler.slnx`

6. Archive the plan file
   - verify: move this file into `docs/implementation/archive/`
