using ClosedXML.Excel;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Tests.ViewModel;

public class CourseGroupsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteRepository _repo;
    private readonly WorkspaceService _workspace;

    public CourseGroupsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cg_test_{Guid.NewGuid():N}.db");
        _repo = new SqliteRepository(_dbPath);
        _workspace = new WorkspaceService(_repo);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static Course MakeCourse(string id, int section, bool isFixed = false) => new()
    {
        Id = id, Name = "Test", Grade = 2, HoursPerWeek = 3, Section = section,
        CourseType = "전필", IsFixed = isFixed,
        BlockStructure = new List<int> { 2, 1 },
    };

    private DataInputViewModel MakeVm() =>
        new(_workspace, null!);

    private void AddTwoSectionCourse(string baseId, Action<Course>? configure = null)
    {
        for (var section = 1; section <= 2; section++)
        {
            var course = MakeCourse($"{baseId}-{section:D2}", section);
            configure?.Invoke(course);
            _workspace.AddCourse(course);
        }
    }

    private static string CreateWorkbookWithRooms(params (string Grade, string Type, string Name, string Hours, string Code, string Professor, string Department, string Schedule)[] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sheet1");
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var excelRow = index + 1;
            sheet.Cell(excelRow, "E").Value = row.Grade;
            sheet.Cell(excelRow, "F").Value = row.Type;
            sheet.Cell(excelRow, "G").Value = row.Name;
            sheet.Cell(excelRow, "H").Value = row.Hours;
            sheet.Cell(excelRow, "I").Value = row.Code;
            sheet.Cell(excelRow, "Q").Value = row.Professor;
            sheet.Cell(excelRow, "R").Value = row.Department;
            sheet.Cell(excelRow, "S").Value = row.Schedule;
        }
        workbook.SaveAs(path);
        return path;
    }

    [Fact]
    public void Solver_DefaultsTo120SecondTotalWithUnlimitedPerSolutionSearch()
    {
        var options = new DiverseSolverOptions();
        var vm = MakeVm();

        Assert.Equal(120, options.TimeLimitSec);
        Assert.Equal(0, options.PerSolveTimeSec);
        Assert.Equal(120, vm.TimeLimitSec);
    }

    [Fact]
    public void ConsiderRetakeStudents_DefaultsDisabled()
    {
        var vm = MakeVm();

        Assert.False(vm.ConsiderRetakeStudents);
    }

    [Fact]
    public void GradeOptionsAndCourseHeader_IncludeGraduateLevel()
    {
        var course = MakeCourse("GR1001-01", 1);
        course.Grade = AcademicLevels.GraduateGrade;
        _workspace.AddCourse(course);

        var vm = MakeVm();

        Assert.Contains(vm.GradeOptions, option =>
            option.Grade == AcademicLevels.GraduateGrade && option.DisplayName == "대학원");
        var group = Assert.Single(vm.CourseGroups);
        Assert.Equal("대학원", group.HeaderGrade);
        Assert.Contains("대학원", group.DisplayLabel);
    }

    [Fact]
    public void NonFixed_TwoSections_ProducesOneGroupRow()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));

        var vm = MakeVm();

        Assert.Single(vm.CourseGroups);
        Assert.Equal(2, vm.CourseGroups[0].Sections.Count);
        Assert.Equal("GA1004", vm.CourseGroups[0].BaseId);
        Assert.Equal("2개", vm.CourseGroups[0].HeaderSectionInfo);
        Assert.False(vm.CourseGroups[0].IsFixedIndividual);
    }

    [Fact]
    public void CourseEditorItems_AreViewModelsNotRawCourses()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        var vm = MakeVm();

        var item = Assert.Single(vm.CourseGroups);

        Assert.IsType<CourseGroupItem>(item);
        Assert.IsNotType<Course>(item);
    }

    [Fact]
    public void CourseEditorItem_ProvidesBlockSummaryAndSectionEditors()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1, isFixed: true));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2, isFixed: true));
        var vm = MakeVm();

        var item = Assert.Single(vm.CourseGroups);

        Assert.NotNull(item.FixedSlotEditor);
        Assert.Contains("블록 구성", item.FixedSlotEditor.BlockSummary);
        Assert.Equal(2, item.FixedSlotEditor.SectionEditors.Count);
    }

    [Fact]
    public void CourseDisplay_DoesNotBindMissingCourseProperties()
    {
        Assert.Null(typeof(Course).GetProperty("BlockSummary"));
        Assert.Null(typeof(Course).GetProperty("SectionEditors"));
        Assert.NotNull(typeof(CourseGroupItem).GetProperty("FixedSlotEditor"));
    }

    [Fact]
    public void AddNew_Course_GeneratesNextNumericId()
    {
        _workspace.AddCourse(MakeCourse("1-01", 1));
        var vm = MakeVm();
        vm.SelectedCategory = InputCategory.Course;
        vm.NewName = "자동생성과목";

        vm.AddNewCommand.Execute(null);

        var added = _workspace.Courses.Single(c => c.Name == "자동생성과목");
        Assert.Equal("2", added.Id);
        Assert.Equal(1, added.Section);
    }

    [Fact]
    public void AddNew_EmptyCourseName_ReportsIe001AndDoesNotAdd()
    {
        var vm = MakeVm();
        vm.SelectedCategory = InputCategory.Course;
        vm.NewName = " ";

        vm.AddNewCommand.Execute(null);

        Assert.Empty(_workspace.Courses);
        Assert.Contains("IE-001", vm.StatusMessage);
    }

    [Fact]
    public void CourseFixedRooms_HidesEmptyRoomMetadata()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "D330" });
        _workspace.AddRoom(new Room { Id = "R2", Name = "LAB1", IsLab = true });
        _workspace.AddRoom(new Room { Id = "R3", Name = "D331", Capacity = 40 });
        var course = MakeCourse("GA1005-01", 1);
        course.FixedRooms = new List<string> { "R1", "R2", "R3" };
        _workspace.AddCourse(course);

        var vm = MakeVm();

        Assert.Equal("D330, LAB1, D331", vm.CourseGroups.Single().HeaderFixedRooms);
    }

    [Fact]
    public void EditProfessor_AndSaveProfessor_TogglesEditingState()
    {
        _workspace.AddProfessor(new Professor
        {
            Id = "P1",
            Name = "교수1",
        });

        var vm = MakeVm();
        var item = vm.ProfessorItems.Single();

        vm.EditProfessorCommand.Execute(item);

        Assert.True(item.IsEditing);

        vm.SaveProfessorCommand.Execute(item);

        Assert.False(item.IsEditing);
    }

    [Fact]
    public void Professor_UsesNoneSummaryWhenUnavailableSlotsAreEmpty()
    {
        _workspace.AddProfessor(new Professor
        {
            Id = "P1",
            Name = "교수1",
        });

        var vm = MakeVm();

        Assert.Equal("없음", vm.ProfessorItems.Single().HeaderUnavailableSlots);
    }

    [Fact]
    public void RoomRows_MarkFixedRoomsAsImportedEvenWhenDepartmentIsBlank()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "D330" });
        _workspace.AddRoom(new Room { Id = "R2", Name = "수동강의실" });
        var course = MakeCourse("1-01", 1);
        course.FixedRooms = new List<string> { "R1" };
        _workspace.AddCourse(course);

        var vm = MakeVm();

        Assert.True(vm.RoomItems.Single(r => r.Room.Id == "R1").IsImportedFromExcel);
        Assert.False(vm.RoomItems.Single(r => r.Room.Id == "R2").IsImportedFromExcel);
    }

    [Fact]
    public void RoomRows_UseImportedRoomSetAfterXlsxImport()
    {
        var path = CreateWorkbookWithRooms(
            ("2", "필수", "자료구조", "3", "GA1004", "홍길동", "컴퓨터", "월23/A101"),
            ("2", "필수", "운영체제", "3", "GA1005", "홍길동", "컴퓨터", "화23/B202"));
        try
        {
            _workspace.ImportFromXlsx(path);
            var importedRoomId = _workspace.Rooms.First(r => r.Name == "B202").Id;
            _workspace.Courses.Single(c => c.Name == "운영체제").FixedRooms.Clear();
            _workspace.Reload();
            _workspace.AddRoom(new Room { Id = "manual", Name = "수동강의실" });

            var vm = MakeVm();

            Assert.True(vm.RoomItems.Single(r => r.Room.Id == importedRoomId).IsImportedFromExcel);
            Assert.False(vm.RoomItems.Single(r => r.Room.Id == "manual").IsImportedFromExcel);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EditRoom_AndSaveRoom_TogglesEditingState()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "강의실1" });

        var vm = MakeVm();
        var item = vm.RoomItems.Single();

        vm.EditRoomCommand.Execute(item);

        Assert.True(item.IsEditing);

        vm.SaveRoomCommand.Execute(item);

        Assert.False(item.IsEditing);
    }

    [Fact]
    public void NonFixed_OneSection_ProducesOneGroupRow()
    {
        _workspace.AddCourse(MakeCourse("GA1005-01", 1));

        var vm = MakeVm();

        Assert.Single(vm.CourseGroups);
        Assert.Single(vm.CourseGroups[0].Sections);
    }

    [Fact]
    public void Fixed_TwoSections_RemainsOneGroupRow()
    {
        _workspace.AddCourse(MakeCourse("GA1005-01", 1, isFixed: true));
        _workspace.AddCourse(MakeCourse("GA1005-02", 2, isFixed: true));

        var vm = MakeVm();

        Assert.Single(vm.CourseGroups);
        Assert.Equal(2, vm.CourseGroups[0].Sections.Count);
        Assert.False(vm.CourseGroups[0].IsFixedIndividual);
        Assert.Contains("★", vm.CourseGroups[0].DisplayLabel);
    }

    [Fact]
    public void Mixed_FixedAndNonFixed_StaysGroupedByBaseCourse()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));           // non-fixed group
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        _workspace.AddCourse(MakeCourse("GA1005-01", 1, isFixed: true));
        _workspace.AddCourse(MakeCourse("GA1005-02", 2, isFixed: true));

        var vm = MakeVm();

        Assert.Equal(2, vm.CourseGroups.Count);
    }

    [Fact]
    public void CourseGroups_UpdatesWhenWorkspaceChanges()
    {
        var vm = MakeVm();
        Assert.Empty(vm.CourseGroups);

        _workspace.AddCourse(MakeCourse("GA1004-01", 1));

        Assert.Single(vm.CourseGroups);
    }

    [Fact]
    public void DisplayLabel_NonFixed_ContainsSectionLetters()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));

        var vm = MakeVm();

        Assert.Contains("A·B", vm.CourseGroups[0].DisplayLabel);
    }

    [Fact]
    public void DisplayLabel_Fixed_ContainsStar()
    {
        _workspace.AddCourse(MakeCourse("GA1005-01", 1, isFixed: true));

        var vm = MakeVm();

        Assert.Contains("★", vm.CourseGroups[0].DisplayLabel);
        Assert.Equal("★", vm.CourseGroups[0].HeaderFixedMarker);
    }

    [Fact]
    public void CourseGroup_FormatsBlockStructureWithPlus()
    {
        _workspace.AddCourse(MakeCourse("GA1005-01", 1));

        var vm = MakeVm();

        Assert.Equal("2+1", vm.CourseGroups[0].HeaderBlockStructure);
        Assert.Equal("2+1", vm.CourseGroups[0].ReadOnlyBlockStructure);
    }

    [Fact]
    public void GenerateBlockStructureOptions_UsesWeeklyHoursExamples()
    {
        var vm = MakeVm();

        Assert.Contains(5, vm.HourOptions);
        Assert.Equal(new[] { "2" }, DataInputViewModel.GenerateBlockStructureOptions(2));
        Assert.Equal(new[] { "1+2", "3" }, DataInputViewModel.GenerateBlockStructureOptions(3));
        Assert.Equal(new[] { "2+2", "4" }, DataInputViewModel.GenerateBlockStructureOptions(4));
        Assert.Equal(new[] { "1+2+2", "2+3" }, DataInputViewModel.GenerateBlockStructureOptions(5));
    }

    [Fact]
    public void HandleCourseHoursChanged_ResetsDefaultBlocksAndClearsFixedTime()
    {
        var course = MakeCourse("GA1005-01", 1);
        course.HoursPerWeek = 4;
        course.BlockStructure = new List<int> { 3, 1 };
        course.IsFixed = true;
        course.FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) };
        _workspace.AddCourse(course);

        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        var changed = vm.HandleCourseHoursChanged(item, 3);

        Assert.True(changed);
        Assert.Equal(3, item.Sections[0].HoursPerWeek);
        Assert.Equal(new[] { 1, 2 }, item.Sections[0].BlockStructure);
        Assert.False(item.Sections[0].IsFixed);
        Assert.Empty(item.Sections[0].FixedSlots);
    }

    [Fact]
    public void HandleCourseHoursChanged_UpdatesAllSectionsToNewDefaultBlocks()
    {
        var first = MakeCourse("GA1005-01", 1);
        var second = MakeCourse("GA1005-02", 2);
        first.HoursPerWeek = 3;
        second.HoursPerWeek = 3;
        first.BlockStructure = new List<int> { 2, 1 };
        second.BlockStructure = new List<int> { 2, 1 };
        first.IsFixed = true;
        second.IsFixed = true;
        first.FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) };
        second.FixedSlots = new List<TimeSlot> { new(2, 1), new(2, 2), new(3, 1) };
        _workspace.AddCourse(first);
        _workspace.AddCourse(second);

        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        var changed = vm.HandleCourseHoursChanged(item, 4);

        Assert.True(changed);
        Assert.All(item.Sections, section => Assert.Equal(4, section.HoursPerWeek));
        Assert.All(item.Sections, section => Assert.Equal(new[] { 2, 2 }, section.BlockStructure));
        Assert.All(item.Sections, section => Assert.False(section.IsFixed));
        Assert.All(item.Sections, section => Assert.Empty(section.FixedSlots));
    }

    [Fact]
    public void HandleCourseBlockStructureChanged_ClearsFixedTime()
    {
        var course = MakeCourse("GA1005-01", 1);
        course.IsFixed = true;
        course.FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) };
        _workspace.AddCourse(course);

        var vm = MakeVm();
        var item = vm.CourseGroups[0];
        item.Sections[0].BlockStructure = new List<int> { 3 };

        vm.HandleCourseBlockStructureChanged(item);

        Assert.False(item.Sections[0].IsFixed);
        Assert.Empty(item.Sections[0].FixedSlots);
    }

    [Fact]
    public void CourseEdit_DoesNotMutateWorkspaceUntilSave()
    {
        var course = MakeCourse("GA1005-01", 1, isFixed: true);
        course.FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) };
        _workspace.AddCourse(course);

        var vm = MakeVm();
        var item = vm.CourseGroups.Single();

        item.Sections[0].Grade = 4;
        item.Sections[0].IsFixed = false;
        item.Sections[0].FixedSlots.Clear();

        Assert.Equal(2, _workspace.Courses.Single().Grade);
        Assert.True(_workspace.Courses.Single().IsFixed);
        Assert.NotEmpty(_workspace.Courses.Single().FixedSlots);

        vm.SaveGroupCommand.Execute(item);

        Assert.Equal(4, _workspace.Courses.Single().Grade);
        Assert.False(_workspace.Courses.Single().IsFixed);
        Assert.Empty(_workspace.Courses.Single().FixedSlots);
    }

    [Fact]
    public void ProfessorAndRoomEdits_DoNotMutateWorkspaceUntilSave()
    {
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Prof One" });
        _workspace.AddRoom(new Room { Id = "R1", Name = "Room One", Capacity = 30 });

        var vm = MakeVm();
        var professor = vm.ProfessorItems.Single();
        var room = vm.RoomItems.Single();

        professor.Professor.UnavailableSlots.Add(new TimeSlot(0, 1));
        room.Room.Capacity = 45;
        room.Room.IsLab = true;

        Assert.Empty(_workspace.Professors.Single().UnavailableSlots);
        Assert.Equal(30, _workspace.Rooms.Single().Capacity);
        Assert.False(_workspace.Rooms.Single().IsLab);

        vm.SaveProfessorCommand.Execute(professor);
        vm.SaveRoomCommand.Execute(room);

        Assert.Equal(new TimeSlot(0, 1), _workspace.Professors.Single().UnavailableSlots.Single());
        Assert.Equal(45, _workspace.Rooms.Single().Capacity);
        Assert.True(_workspace.Rooms.Single().IsLab);
    }

    [Fact]
    public void CancelItemEdits_RestoresSavedCopiesAndStopsEditing()
    {
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor" });
        _workspace.AddRoom(new Room { Id = "R1", Name = "Room", Capacity = 30 });
        _workspace.AddCourse(MakeCourse("C-01", 1));

        var vm = MakeVm();
        var course = vm.CourseGroups.Single();
        var professor = vm.ProfessorItems.Single();
        var room = vm.RoomItems.Single();

        vm.EditCourseCommand.Execute(course);
        vm.EditProfessorCommand.Execute(professor);
        vm.EditRoomCommand.Execute(room);
        course.Sections[0].Grade = 4;
        professor.Professor.UnavailableSlots.Add(new TimeSlot(0, 1));
        room.Room.Capacity = 45;
        room.Room.IsLab = true;

        vm.CancelGroupCommand.Execute(course);
        vm.CancelProfessorCommand.Execute(professor);
        vm.CancelRoomCommand.Execute(room);

        Assert.False(course.IsEditing);
        Assert.False(professor.IsEditing);
        Assert.False(room.IsEditing);
        Assert.Equal(2, course.Sections[0].Grade);
        Assert.Empty(professor.Professor.UnavailableSlots);
        Assert.Equal(30, room.Room.Capacity);
        Assert.False(room.Room.IsLab);
        Assert.Equal(2, _workspace.Courses.Single().Grade);
        Assert.Empty(_workspace.Professors.Single().UnavailableSlots);
        Assert.Equal(30, _workspace.Rooms.Single().Capacity);
        Assert.False(_workspace.Rooms.Single().IsLab);
    }

    [Fact]
    public void CourseGroup_UsesBlankSummaryValuesWhenOptionalFieldsAreEmpty()
    {
        _workspace.AddCourse(MakeCourse("GA1005-01", 1));

        var vm = MakeVm();
        var group = vm.CourseGroups[0];

        Assert.Equal(string.Empty, group.HeaderProfessor);
        Assert.Equal(string.Empty, group.HeaderUnavailableRooms);
        Assert.Equal(string.Empty, group.HeaderFixedRooms);
        Assert.Equal(string.Empty, group.HeaderCoteachProfessors);
        Assert.Equal(string.Empty, group.HeaderFixedTimes);
        Assert.Equal("없음", group.ReadOnlyProfessor);
        Assert.Equal("없음", group.ReadOnlyUnavailableRooms);
        Assert.Equal("없음", group.ReadOnlyFixedRooms);
        Assert.Equal("없음", group.ReadOnlyCoteachProfessors);
        Assert.Equal("없음", group.ReadOnlyFixedTimes);
    }

    [Fact]
    public void SaveGroup_WhenUnfixed_ClearsFixedSlotsAndReadOnlyText()
    {
        var first = MakeCourse("GA1005-01", 1, isFixed: true);
        var second = MakeCourse("GA1005-02", 2, isFixed: true);
        first.FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) };
        second.FixedSlots = new List<TimeSlot> { new(2, 1), new(2, 2), new(3, 1) };
        _workspace.AddCourse(first);
        _workspace.AddCourse(second);

        var vm = MakeVm();
        var item = vm.CourseGroups.Single();
        item.Sections[0].IsFixed = false;

        vm.SaveGroupCommand.Execute(item);

        var saved = _workspace.Courses.OrderBy(c => c.Section).ToList();
        Assert.All(saved, section => Assert.False(section.IsFixed));
        Assert.All(saved, section => Assert.Empty(section.FixedSlots));
        Assert.Equal("없음", vm.CourseGroups.Single().ReadOnlyFixedTimes);
    }

    [Fact]
    public void CourseGroup_KeepsSectionProfessorsIndependent()
    {
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Prof One" });
        _workspace.AddProfessor(new Professor { Id = "P2", Name = "Prof Two" });
        var sectionA = MakeCourse("GA1005-01", 1);
        var sectionB = MakeCourse("GA1005-02", 2);
        sectionA.ProfessorId = "P1";
        sectionB.ProfessorId = "P2";
        _workspace.AddCourse(sectionA);
        _workspace.AddCourse(sectionB);

        var vm = MakeVm();

        var group = Assert.Single(vm.CourseGroups);
        Assert.Contains("A분반: Prof One", group.HeaderProfessor);
        Assert.Contains("B분반: Prof Two", group.HeaderProfessor);
        Assert.Equal("P1", group.Sections[0].ProfessorId);
        Assert.Equal("P2", group.Sections[1].ProfessorId);
    }

    [Fact]
    public void SaveGroup_ChangingOneSectionProfessor_DoesNotChangeSiblingSection()
    {
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Prof One" });
        _workspace.AddProfessor(new Professor { Id = "P2", Name = "Prof Two" });
        _workspace.AddProfessor(new Professor { Id = "P3", Name = "Prof Three" });
        var sectionA = MakeCourse("GA1005-01", 1);
        var sectionB = MakeCourse("GA1005-02", 2);
        sectionA.ProfessorId = "P1";
        sectionB.ProfessorId = "P2";
        _workspace.AddCourse(sectionA);
        _workspace.AddCourse(sectionB);

        var vm = MakeVm();
        var group = Assert.Single(vm.CourseGroups);
        group.Sections[0].ProfessorId = "P3";

        vm.SaveGroupCommand.Execute(group);

        var saved = _workspace.Courses.OrderBy(course => course.Section).ToList();
        Assert.Equal("P3", saved[0].ProfessorId);
        Assert.Equal("P2", saved[1].ProfessorId);
    }

    [Fact]
    public void SchedulingSnapshot_PreservesSectionProfessorIds()
    {
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Prof One" });
        _workspace.AddProfessor(new Professor { Id = "P2", Name = "Prof Two" });
        var sectionA = MakeCourse("GA1005-01", 1);
        var sectionB = MakeCourse("GA1005-02", 2);
        sectionA.ProfessorId = "P1";
        sectionB.ProfessorId = "P2";
        _workspace.AddCourse(sectionA);
        _workspace.AddCourse(sectionB);

        var vm = MakeVm();
        var group = Assert.Single(vm.CourseGroups);

        vm.SaveGroupCommand.Execute(group);

        var snapshot = _workspace.SchedulingSnapshot();
        var saved = snapshot.Courses.OrderBy(course => course.Section).ToList();
        Assert.Equal("P1", saved[0].ProfessorId);
        Assert.Equal("P2", saved[1].ProfessorId);
    }

    [Fact]
    public void FindFixedTimeOverlap_ReturnsExistingFixedCourseConflict()
    {
        var fixedCourse = MakeCourse("GA1005-01", 1, isFixed: true);
        fixedCourse.Name = "기존고정";
        fixedCourse.FixedSlots = new List<TimeSlot> { new(1, 2), new(1, 3), new(2, 1) };
        _workspace.AddCourse(fixedCourse);

        _workspace.AddCourse(MakeCourse("GA1006-01", 1));
        var vm = MakeVm();
        var group = vm.CourseGroups.Single(g => g.BaseId == "GA1006");
        group.Sections[0].IsFixed = true;

        var overlap = vm.FindFixedTimeOverlap(
            group,
            new List<IReadOnlyList<TimeSlot>>
            {
                new List<TimeSlot> { new(1, 2), new(1, 3), new(3, 1) }
            });

        Assert.NotNull(overlap);
        Assert.Equal("GA1005-01", overlap!.ExistingCourseId);
        Assert.Equal("기존고정", overlap.ExistingCourseName);
        Assert.Equal(1, overlap.ExistingSection);
        Assert.Equal(1, overlap.Day);
        Assert.Equal(2, overlap.Period);
    }

    [Fact]
    public void FindFixedTimeOverlap_ReturnsNull_WhenNoOverlapExists()
    {
        var fixedCourse = MakeCourse("GA1005-01", 1, isFixed: true);
        fixedCourse.FixedSlots = new List<TimeSlot> { new(1, 2), new(1, 3), new(2, 1) };
        _workspace.AddCourse(fixedCourse);

        _workspace.AddCourse(MakeCourse("GA1006-01", 1));
        var vm = MakeVm();
        var group = vm.CourseGroups.Single(g => g.BaseId == "GA1006");
        group.Sections[0].IsFixed = true;

        var overlap = vm.FindFixedTimeOverlap(
            group,
            new List<IReadOnlyList<TimeSlot>>
            {
                new List<TimeSlot> { new(0, 1), new(0, 2), new(3, 1) }
            });

        Assert.Null(overlap);
    }

    [Fact]
    public void FindFixedTimeOverlap_ReturnsConflict_WhenCandidateSectionsOverlapEachOther()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        var vm = MakeVm();
        var group = vm.CourseGroups.Single(g => g.BaseId == "GA1004");
        group.Sections[0].IsFixed = true;

        var overlap = vm.FindFixedTimeOverlap(
            group,
            new List<IReadOnlyList<TimeSlot>>
            {
                new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) },
                new List<TimeSlot> { new(0, 1), new(0, 2), new(2, 1) },
            });

        Assert.NotNull(overlap);
        Assert.Equal("GA1004-02", overlap!.ExistingCourseId);
        Assert.Equal(0, overlap.Day);
        Assert.Equal(1, overlap.Period);
    }

    [Fact]
    public void AddSection_AddsNextSectionAndRebuildsGroups()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.AddSectionCommand.Execute(item);

        Assert.Single(vm.CourseGroups); // still 1 group row
        Assert.Equal(2, vm.CourseGroups[0].Sections.Count);
        Assert.Equal("GA1004-02", vm.CourseGroups[0].Sections[1].Id);
    }

    [Fact]
    public void AddSection_CopiesSharedFieldsFromFirstSection()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.AddSectionCommand.Execute(item);

        var added = vm.CourseGroups[0].Sections[1];
        Assert.Equal("Test", added.Name);
        Assert.Equal(2, added.Grade);
        Assert.Equal(3, added.HoursPerWeek);
        Assert.Equal("전필", added.CourseType);
    }

    [Fact]
    public void AddSection_WhenCourseIsFixed_ClearsFixedTimeFromExistingSection()
    {
        var course = MakeCourse("GA1004-01", 1, isFixed: true);
        course.FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) };
        _workspace.AddCourse(course);
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.AddSectionCommand.Execute(item);

        var saved = _workspace.Courses.OrderBy(c => c.Section).ToList();
        Assert.Equal(2, saved.Count);
        Assert.All(saved, section => Assert.False(section.IsFixed));
        Assert.All(saved, section => Assert.Empty(section.FixedSlots));
        Assert.Equal("", vm.CourseGroups[0].HeaderFixedMarker);
        Assert.Equal("없음", vm.CourseGroups[0].ReadOnlyFixedTimes);
    }

    [Fact]
    public void RemoveSection_RemovesLastSection()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.RemoveSectionCommand.Execute(item);

        Assert.Single(vm.CourseGroups);
        Assert.Single(vm.CourseGroups[0].Sections);
        Assert.Equal("GA1004-01", vm.CourseGroups[0].Sections[0].Id);
    }

    [Fact]
    public void RemoveSection_WhenCourseIsFixed_ClearsFixedTimeFromRemainingSections()
    {
        var first = MakeCourse("GA1004-01", 1, isFixed: true);
        first.FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) };
        var second = MakeCourse("GA1004-02", 2, isFixed: true);
        second.FixedSlots = new List<TimeSlot> { new(2, 1), new(2, 2), new(3, 1) };
        _workspace.AddCourse(first);
        _workspace.AddCourse(second);
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.RemoveSectionCommand.Execute(item);

        var saved = Assert.Single(_workspace.Courses);
        Assert.Equal("GA1004-01", saved.Id);
        Assert.False(saved.IsFixed);
        Assert.Empty(saved.FixedSlots);
        Assert.Equal("", vm.CourseGroups[0].HeaderFixedMarker);
        Assert.Equal("없음", vm.CourseGroups[0].ReadOnlyFixedTimes);
    }

    [Fact]
    public void RemoveSection_NoOp_WhenOnlyOneSection()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        vm.RemoveSectionCommand.Execute(item);

        Assert.Single(vm.CourseGroups[0].Sections); // unchanged
        Assert.Equal("GA1004-01", _workspace.Courses[0].Id);
    }

    [Fact]
    public void SaveGroup_PropagatesIsFixed_ToAllSections_WithoutSplittingTheGroup()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];
        item.Sections[0].IsFixed = true;
        item.Sections[0].FixedSlots = new List<TimeSlot> { new(0, 1), new(0, 2), new(1, 1) };

        vm.SaveGroupCommand.Execute(item);

        var courses = _workspace.Courses.OrderBy(c => c.Id).ToList();
        Assert.True(courses[0].IsFixed);
        Assert.True(courses[1].IsFixed);
        Assert.Single(vm.CourseGroups);
        Assert.Equal(2, vm.CourseGroups[0].Sections.Count);
    }

    [Fact]
    public void SaveGroup_PropagatesUnavailableRooms_ToAllSectionsAndStopsEditing()
    {
        _workspace.AddCourse(MakeCourse("GA1004-01", 1));
        _workspace.AddCourse(MakeCourse("GA1004-02", 2));
        var vm = MakeVm();
        var item = vm.CourseGroups[0];
        item.IsEditing = true;
        item.Sections[0].FixedRooms = new List<string> { "R1" };
        item.Sections[0].UnavailableRooms = new List<string> { "R2" };

        vm.SaveGroupCommand.Execute(item);

        var courses = _workspace.Courses.OrderBy(c => c.Id).ToList();
        Assert.Equal(new[] { "R1" }, courses[0].FixedRooms);
        Assert.Equal(new[] { "R1" }, courses[1].FixedRooms);
        Assert.Equal(new[] { "R2" }, courses[0].UnavailableRooms);
        Assert.Equal(new[] { "R2" }, courses[1].UnavailableRooms);
        Assert.False(item.IsEditing);
    }

    [Fact]
    public void SaveGroup_ClearsSharedOptionalLists_ToAllSections()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "Room 1" });
        _workspace.AddRoom(new Room { Id = "R2", Name = "Room 2" });
        _workspace.AddProfessor(new Professor { Id = "P2", Name = "Co Professor" });
        var first = MakeCourse("GA1004-01", 1);
        var second = MakeCourse("GA1004-02", 2);
        foreach (var course in new[] { first, second })
        {
            course.FixedRooms = new List<string> { "R1" };
            course.UnavailableRooms = new List<string> { "R2" };
            course.CoteachProfs = new List<string> { "P2" };
        }
        _workspace.AddCourse(first);
        _workspace.AddCourse(second);
        var vm = MakeVm();
        var item = vm.CourseGroups[0];

        item.Sections[0].FixedRooms.Clear();
        item.Sections[0].UnavailableRooms.Clear();
        item.Sections[0].CoteachProfs.Clear();
        vm.SaveGroupCommand.Execute(item);

        var courses = _workspace.Courses.OrderBy(c => c.Id).ToList();
        Assert.All(courses, course => Assert.Empty(course.FixedRooms));
        Assert.All(courses, course => Assert.Empty(course.UnavailableRooms));
        Assert.All(courses, course => Assert.Empty(course.CoteachProfs));
    }

    [Fact]
    public void LoadForExistingTimetable_UsesSavedSnapshotForProfessorAndCourseType()
    {
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "스냅교수" });
        _workspace.AddCourse(new Course
        {
            Id = "GA1005-01",
            Name = "저장과목",
            Grade = 2,
            HoursPerWeek = 3,
            Section = 1,
            CourseType = "전필",
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1, 2 },
        });

        _workspace.SaveTimetable(
            "saved",
            new[] { new TimetableScheduler.Solver.SolutionAssignment("GA1005-01", 0, 1, "R1") },
            snapshot: _workspace.Snapshot());

        _workspace.DeleteCourse(_workspace.Courses.Single());
        _workspace.DeleteProfessor("P1");

        var vm = MakeVm();
        var saved = _workspace.SavedTimetables.Single(t => t.Name == "saved");

        vm.LoadForExistingTimetable(saved);

        var group = Assert.Single(vm.CourseGroups);
        Assert.Equal("스냅교수", group.HeaderProfessor);
        Assert.Equal("전필", group.HeaderCourseType);
        Assert.Equal("전필", group.Sections[0].CourseType);
    }

    [Fact]
    public void LoadForExistingTimetable_InvalidSnapshotWithBlankProfessorAndCourseType_ShowsNoCourses()
    {
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "라이브교수" });
        _workspace.AddCourse(new Course
        {
            Id = "GA1005-01",
            Name = "저장과목",
            Grade = 2,
            HoursPerWeek = 3,
            Section = 1,
            CourseType = "전필",
            ProfessorId = "P1",
            BlockStructure = new List<int> { 1, 2 },
        });

        var incompleteSnapshot = new AppData(
            new List<Course>
            {
                new()
                {
                    Id = "GA1005-01",
                    Name = "저장과목",
                    Grade = 2,
                    HoursPerWeek = 3,
                    Section = 1,
                    CourseType = "",
                    ProfessorId = "",
                    BlockStructure = new List<int> { 1, 2 },
                },
            },
            new List<Professor>(),
            new List<Room>(),
            new List<CrossGroup>(),
            new List<RetakeScenario>());

        var saved = new SavedTimetableRecord(
            "saved-merge",
            "saved",
            DateTime.Now,
            new[] { new TimetableScheduler.Solver.SolutionAssignment("GA1005-01", 0, 1, "R1") }
                .Select(a => new TimetableAssignmentRow(a.CourseId, a.Day, a.Period, a.RoomId))
                .ToList(),
            SnapshotJson: System.Text.Json.JsonSerializer.Serialize(incompleteSnapshot));

        var vm = MakeVm();

        vm.LoadForExistingTimetable(saved);

        Assert.Empty(vm.CourseGroups);
    }

    [Fact]
    public void CrossManager_AddsSelectedCoursesAndRebuildsLists()
    {
        AddTwoSectionCourse("A");
        AddTwoSectionCourse("B");
        var vm = MakeVm();

        Assert.Equal(new[] { "A", "B" }, vm.CrossCandidateItems.Select(i => i.Id));
        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.CrossCandidateItems.Single(i => i.Id == "B").IsChecked = true;

        vm.AddCrossCommand.Execute(null);

        var cross = Assert.Single(_workspace.CrossGroups);
        Assert.Equal("X001", cross.Id);
        Assert.Equal(new[] { "A", "B" }, cross.BaseIds);
        Assert.Single(vm.CrossGroupItems);
        Assert.Contains("A(Test)", vm.CrossGroupItems[0].Display);
        Assert.All(vm.CrossCandidateItems, item => Assert.False(item.IsChecked));
        Assert.Equal("Cross를 추가했습니다.", vm.CrossStatusMessage);
        Assert.Equal("B", vm.CourseGroups.Single(g => g.BaseId == "A").HeaderCrossGroups);
    }

    [Fact]
    public void CrossManager_DisablesSingleSectionCandidates()
    {
        _workspace.AddCourse(MakeCourse("A-01", 1));
        AddTwoSectionCourse("B");
        var vm = MakeVm();

        Assert.False(vm.CrossCandidateItems.Single(i => i.Id == "A").IsEnabled);
        Assert.True(vm.CrossCandidateItems.Single(i => i.Id == "B").IsEnabled);
    }

    [Fact]
    public void CrossManager_KeepsSelectedCandidatesEnabledAfterTwoSelections()
    {
        AddTwoSectionCourse("A");
        AddTwoSectionCourse("B");
        AddTwoSectionCourse("C");
        var vm = MakeVm();

        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.CrossCandidateItems.Single(i => i.Id == "B").IsChecked = true;

        Assert.True(vm.CrossCandidateItems.Single(i => i.Id == "A").IsEnabled);
        Assert.True(vm.CrossCandidateItems.Single(i => i.Id == "B").IsEnabled);
        Assert.False(vm.CrossCandidateItems.Single(i => i.Id == "C").IsEnabled);

        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = false;

        Assert.False(vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked);
        Assert.True(vm.CrossCandidateItems.Single(i => i.Id == "C").IsEnabled);
    }

    [Fact]
    public void CrossManager_RejectsSingleSectionSelectionWhenExecutedProgrammatically()
    {
        _workspace.AddCourse(MakeCourse("A-01", 1));
        AddTwoSectionCourse("B");
        var vm = MakeVm();

        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.CrossCandidateItems.Single(i => i.Id == "B").IsChecked = true;
        vm.AddCrossCommand.Execute(null);

        Assert.Empty(_workspace.CrossGroups);
        Assert.Contains("분반이 2개 이상", vm.CrossStatusMessage);
    }

    [Fact]
    public void CrossManager_DeleteSelectedCross_RemovesWorkspaceGroup()
    {
        AddTwoSectionCourse("A");
        AddTwoSectionCourse("B");
        _workspace.AddCrossGroup(new CrossGroup { Id = "legacy", BaseIds = new List<string> { "A", "B" } });
        var vm = MakeVm();

        var item = Assert.Single(vm.CrossGroupItems);
        vm.DeleteCrossCommand.Execute(item);

        Assert.Empty(_workspace.CrossGroups);
        Assert.Empty(vm.CrossGroupItems);
        Assert.Equal("선택한 Cross를 삭제했습니다.", vm.CrossStatusMessage);
    }

    [Fact]
    public void CrossManager_RejectsInvalidSelection()
    {
        AddTwoSectionCourse("A");
        AddTwoSectionCourse("B", course => course.HoursPerWeek = 4);
        var vm = MakeVm();

        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.AddCrossCommand.Execute(null);
        Assert.Equal("Cross는 과목 2개만 선택할 수 있습니다.", vm.CrossStatusMessage);

        vm.CrossCandidateItems.Single(i => i.Id == "B").IsChecked = true;
        vm.AddCrossCommand.Execute(null);

        Assert.Empty(_workspace.CrossGroups);
        Assert.Equal("Cross로 묶으려면 총 시수가 동일해야 합니다.", vm.CrossStatusMessage);
    }

    [Fact]
    public void CrossManager_RejectsDifferentBlockStructures()
    {
        AddTwoSectionCourse("A", course => course.BlockStructure = new List<int> { 3 });
        AddTwoSectionCourse("B", course => course.BlockStructure = new List<int> { 2, 1 });
        var vm = MakeVm();

        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.CrossCandidateItems.Single(i => i.Id == "B").IsChecked = true;
        vm.AddCrossCommand.Execute(null);

        Assert.Empty(_workspace.CrossGroups);
        Assert.Contains("블록구조가 같아야 합니다", vm.CrossStatusMessage);
        Assert.Contains("A: 3", vm.CrossStatusMessage);
        Assert.Contains("B: 2+1", vm.CrossStatusMessage);
    }

    [Fact]
    public void CrossManager_RejectsDuplicateMembership()
    {
        AddTwoSectionCourse("A");
        AddTwoSectionCourse("B");
        AddTwoSectionCourse("C");
        _workspace.AddCrossGroup(new CrossGroup { Id = "X001", BaseIds = new List<string> { "A", "B" } });
        var vm = MakeVm();

        vm.CrossCandidateItems.Single(i => i.Id == "A").IsChecked = true;
        vm.CrossCandidateItems.Single(i => i.Id == "C").IsChecked = true;
        vm.AddCrossCommand.Execute(null);

        Assert.Single(_workspace.CrossGroups);
        Assert.Equal("이미 다른 Cross에 속한 과목이 있습니다: A", vm.CrossStatusMessage);
    }

    [Fact]
    public async Task Solve_WithUnsavedEdits_ReportsIe037LocationsAndDoesNotRunSolver()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "Room" });
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor" });
        _workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "Course",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        var vm = new DataInputViewModel(_workspace, null!);
        vm.CourseGroups.Single().IsEditing = true;
        vm.ProfessorItems.Single().IsEditing = true;
        vm.RoomItems.Single().IsEditing = true;

        await vm.SolveCommand.ExecuteAsync(null);

        Assert.Contains("IE-037", vm.StatusMessage);
        Assert.Contains("교과목 관리 > Course (C)", vm.StatusMessage);
        Assert.Contains("교수 관리 > Professor (P1)", vm.StatusMessage);
        Assert.Contains("강의실 관리 > Room (R1)", vm.StatusMessage);
        Assert.False(vm.IsSolving);
        Assert.False(vm.IsSolveComplete);
    }

    [Fact]
    public async Task Solve_WithNoRooms_ReportsIe027AndDoesNotRunSolver()
    {
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor" });
        _workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "Course",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        var vm = new DataInputViewModel(_workspace, null!);

        await vm.SolveCommand.ExecuteAsync(null);

        Assert.Contains("IE-027", vm.StatusMessage);
        Assert.False(vm.IsSolveComplete);
    }

    [Fact]
    public async Task Solve_WithMissingProfessor_PrioritizesIe004CourseNameInStatus()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "Room" });
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor" });
        for (var index = 1; index <= 5; index++)
        {
            _workspace.AddCourse(new Course
            {
                Id = $"EMPTY-{index:00}",
                Name = "",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                Section = 1,
                BlockStructure = new List<int> { 1 },
            });
        }

        _workspace.AddCourse(new Course
        {
            Id = "C-IE004",
            Name = "Missing Professor Course",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        var vm = new DataInputViewModel(_workspace, null!);

        await vm.SolveCommand.ExecuteAsync(null);

        Assert.Contains("IE-004", vm.StatusMessage);
        Assert.Contains("Missing Professor Course", vm.StatusMessage);
        Assert.False(vm.IsSolveComplete);
    }

    [Fact]
    public async Task Solve_CancelCommand_CompletesWithoutThrowing()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "Room" });
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor" });
        _workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "Course",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        var solver = new CancelAwareSolverService();
        var vm = new DataInputViewModel(_workspace, solver)
        {
            TotalSolutions = 1,
            UseSc01 = false,
            UseSc02 = false,
            UseSc03 = false,
        };
        var cancelCanExecuteStates = new List<bool>();
        vm.CancelCommand.CanExecuteChanged += (_, _) =>
            cancelCanExecuteStates.Add(vm.CancelCommand.CanExecute(null));

        var solveTask = vm.SolveCommand.ExecuteAsync(null);
        await solver.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(vm.IsSolving);
        Assert.Contains(true, cancelCanExecuteStates);
        Assert.True(vm.CancelCommand.CanExecute(null));

        vm.CancelCommand.Execute(null);
        Assert.False(vm.CancelCommand.CanExecute(null));
        await solveTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(vm.IsSolving);
        Assert.False(vm.IsSolveComplete);
        Assert.Equal("취소됨", vm.StatusMessage);
    }

    [Fact]
    public async Task SolverService_WithPreCanceledToken_ReturnsCancelledResult()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "Room" });
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor" });
        _workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "Course",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await new SolverService().SolveAsync(
            _workspace,
            new DiverseSolverOptions { UseSc01 = false, UseSc02 = false, UseSc03 = false },
            cancellationToken: cts.Token);

        Assert.Equal("CANCELLED", result.Status);
        Assert.Empty(result.Solutions);
    }

    [Fact]
    public async Task Solve_InfeasibleFallback_ShowsActionableEditCandidates()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "강의실1" });
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "교수1" });
        _workspace.AddCourse(new Course
        {
            Id = "C-01",
            Name = "과목1",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        var vm = new DataInputViewModel(_workspace, new InfeasibleSolverService())
        {
            TotalSolutions = 1,
            UseSc01 = false,
            UseSc02 = false,
            UseSc03 = false,
        };

        await vm.SolveCommand.ExecuteAsync(null);

        Assert.Contains("GE-025", vm.StatusMessage);
        Assert.Contains("수정 후보", vm.StatusMessage);
        Assert.Contains("시간 고정", vm.StatusMessage);
        Assert.DoesNotContain("detailed reason unavailable", vm.StatusMessage);
    }

    [Fact]
    public async Task Solve_SixGraduateThreeHourCourses_ShowsGe031BeforeSolver()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "강의실1" });
        for (var index = 1; index <= 6; index++)
        {
            var professorId = $"P{index}";
            _workspace.AddProfessor(new Professor { Id = professorId, Name = professorId });
            _workspace.AddCourse(new Course
            {
                Id = $"GR{index}",
                Name = $"대학원 과목 {index}",
                Grade = AcademicLevels.GraduateGrade,
                HoursPerWeek = 3,
                ProfessorId = professorId,
                Section = 1,
                BlockStructure = new List<int> { 3 },
            });
        }
        var vm = new DataInputViewModel(_workspace, null!);

        await vm.SolveCommand.ExecuteAsync(null);

        Assert.Contains("GE-031", vm.StatusMessage);
        Assert.False(vm.IsSolveComplete);
    }

    [Fact]
    public void SchedulingSnapshot_WithCrossPreservesLoadedSectionProfessorMismatch()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "Room 1" });
        _workspace.AddRoom(new Room { Id = "R2", Name = "Room 2" });
        _workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor 1" });
        _workspace.AddProfessor(new Professor { Id = "P2", Name = "Professor 2" });
        _workspace.AddCourse(new Course
        {
            Id = "A-01",
            Name = "A",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        _workspace.AddCourse(new Course
        {
            Id = "A-02",
            Name = "A",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P2",
            Section = 2,
            BlockStructure = new List<int> { 1 },
        });
        _workspace.AddCourse(new Course
        {
            Id = "B-01",
            Name = "B",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P2",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        _workspace.AddCourse(new Course
        {
            Id = "B-02",
            Name = "B",
            Grade = 2,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 2,
            BlockStructure = new List<int> { 1 },
        });
        _workspace.AddCrossGroup(new CrossGroup { Id = "X001", BaseIds = new List<string> { "A", "B" } });
        var snapshot = _workspace.SchedulingSnapshot();
        var byCourse = snapshot.Courses.ToDictionary(course => course.Id);
        Assert.Equal("P1", byCourse["A-01"].ProfessorId);
        Assert.Equal("P2", byCourse["A-02"].ProfessorId);
        Assert.Equal("P2", byCourse["B-01"].ProfessorId);
        Assert.Equal("P1", byCourse["B-02"].ProfessorId);
        Assert.Single(snapshot.CrossGroups);
    }

    [Fact]
    public async Task Solve_Infeasible_DoesNotEnablePreviewOrFireCompleted()
    {
        _workspace.AddRoom(new Room { Id = "R1", Name = "강의실1" });
        _workspace.AddProfessor(new Professor
        {
            Id = "P1",
            Name = "교수1",
            UnavailableRooms = new List<string> { "R1" },
        });
        _workspace.AddCourse(new Course
        {
            Id = "1-01",
            Name = "불가능과목",
            Grade = 1,
            HoursPerWeek = 1,
            ProfessorId = "P1",
            Section = 1,
            BlockStructure = new List<int> { 1 },
        });
        var vm = new DataInputViewModel(_workspace, new SolverService())
        {
            TotalSolutions = 1,
            UseSc01 = false,
            UseSc02 = false,
            UseSc03 = false,
        };
        var completedCount = 0;
        vm.SolveCompleted += (_, _) => completedCount++;

        await vm.SolveCommand.ExecuteAsync(null);

        Assert.False(vm.IsSolveComplete);
        Assert.Empty(vm.RankedResults);
        Assert.Equal(0, completedCount);
        Assert.Contains("IE-023", vm.StatusMessage);
        Assert.DoesNotContain("top", vm.StatusMessage);
    }

    private sealed class CancelAwareSolverService : SolverService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<DiverseSolverResult> SolveAsync(
            WorkspaceService workspace,
            DiverseSolverOptions options,
            IProgress<SolverProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new DiverseSolverResult(
                "UNKNOWN",
                Array.Empty<IReadOnlyList<SolutionAssignment>>(),
                null,
                null,
                null,
                0);
        }
    }

    private sealed class InfeasibleSolverService : SolverService
    {
        public override Task<DiverseSolverResult> SolveAsync(
            WorkspaceService workspace,
            DiverseSolverOptions options,
            IProgress<SolverProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiverseSolverResult(
                "INFEASIBLE",
                Array.Empty<IReadOnlyList<SolutionAssignment>>(),
                null,
                null,
                null,
                0));
        }
    }
}
