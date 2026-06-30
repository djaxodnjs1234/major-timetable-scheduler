using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.Tests.ViewModel;

public class UnifiedTimetableViewModelTests
{
    [Fact]
    public void Render_EmptyAssignment_AllDaysCollapsedToPlaceholder()
    {
        var vm = new UnifiedTimetableViewModel();
        vm.Render(Array.Empty<SolutionAssignment>(), Array.Empty<Course>());

        Assert.Equal(5, vm.DayGroups.Count);
        Assert.All(vm.DayGroups, dg =>
        {
            Assert.Single(dg.Grades);
            Assert.Null(dg.Grades[0].Grade);
        });
    }

    [Fact]
    public void Render_TwoGradesOnOneDay_TwoColumns()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "G2", Grade = 2 },
            new() { Id = "G3", Grade = 3 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("G2", 0, 1, "R"),
            new("G3", 0, 1, "R"),
        };
        vm.Render(assignment, courses);

        // day 0 has grade 2 + grade 3; other days collapsed
        var d0 = vm.DayGroups[0];
        Assert.Equal(2, d0.Grades.Count);
        Assert.Equal(2, d0.Grades[0].Grade);
        Assert.Equal(3, d0.Grades[1].Grade);
    }

    [Fact]
    public void Render_AddsSchoolFixedDisplayBlocksFromCourses()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "G2", Name = "Normal", Grade = 2, HoursPerWeek = 1 },
            new()
            {
                Id = "SF-ALL",
                Name = "All",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                FixedRooms = new List<string> { "R1" },
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 2) },
                IsSchoolFixed = true,
                SchoolFixedTargetGrade = SchoolFixedTimePolicy.AllGrades,
            },
            new()
            {
                Id = "SF-G3",
                Name = "Grade3",
                Grade = 3,
                HoursPerWeek = 1,
                ProfessorId = "P2",
                FixedRooms = new List<string> { "R2" },
                IsFixed = true,
                FixedSlots = new List<TimeSlot> { new(0, 3) },
                IsSchoolFixed = true,
                SchoolFixedTargetGrade = 3,
            },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("G2", 0, 1, "R"),
        };

        vm.Render(assignment, courses);

        Assert.Contains(vm.DayGroups[0].Grades, grade => grade.Grade == 2);
        Assert.Contains(vm.DayGroups[0].Grades, grade => grade.Grade == 3);
        var allGradeCell = Assert.Single(vm.Cells, cell =>
            cell.Grade == 2 &&
            cell.Period == 2 &&
            cell.Assignment.CourseName == "All" &&
            cell.Assignment.IsSchoolFixed);
        var targetGradeCell = Assert.Single(vm.Cells, cell =>
            cell.Grade == 3 &&
            cell.Period == 3 &&
            cell.Assignment.CourseName == "Grade3" &&
            cell.Assignment.IsSchoolFixed);
        Assert.Equal("[학교고정] All", allGradeCell.Assignment.TitleLabel);
        Assert.Equal("", allGradeCell.Assignment.ProfessorLine);
        Assert.Equal("", allGradeCell.Assignment.RoomsLabel);
        Assert.Equal("[학년고정] Grade3", targetGradeCell.Assignment.TitleLabel);
        Assert.Equal("", targetGradeCell.Assignment.ProfessorLine);
        Assert.Equal("", targetGradeCell.Assignment.RoomsLabel);
        Assert.DoesNotContain(vm.Cells, cell => cell.Assignment.CourseId is "SF-ALL" or "SF-G3");
    }

    [Fact]
    public void ExpandAllGrades_True_AllAcademicLevelsShown()
    {
        var vm = new UnifiedTimetableViewModel { ExpandAllGrades = true };
        var courses = new List<Course>
        {
            new() { Id = "G2", Grade = 2 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("G2", 0, 1, "R"),
        };
        vm.Render(assignment, courses);

        // ExpandAllGrades=true shows 1~4 plus graduate even if empty.
        Assert.Equal(AcademicLevels.AllGrades, vm.DayGroups[0].Grades.Select(g => g.Grade!.Value).ToArray());
    }

    [Fact]
    public void Render_GraduateCourse_UsesGraduateColumn()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "GR", Grade = AcademicLevels.GraduateGrade },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("GR", 0, 1, "R"),
        };

        vm.Render(assignment, courses);

        var gradeColumn = Assert.Single(vm.DayGroups[0].Grades);
        Assert.Equal(AcademicLevels.GraduateGrade, gradeColumn.Grade);
        Assert.Equal(AcademicLevels.GraduateGrade, Assert.Single(vm.Cells).Grade);
    }

    [Fact]
    public void Render_TwoSectionsSameSlot_GradeColumnWidthIsTwo()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Grade = 2, Section = 1 },
            new() { Id = "X-02", Grade = 2, Section = 2 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-02", 0, 1, "R2"),
        };
        vm.Render(assignment, courses);

        var d0 = vm.DayGroups[0];
        Assert.Single(d0.Grades);
        Assert.Equal(2, d0.Grades[0].Grade);
        Assert.Equal(2, d0.Grades[0].Width);  // 2 sections → width 2
        Assert.Equal(new[] { "A", "B" }, vm.Cells.Select(cell => cell.Assignment.SectionLabel).OrderBy(label => label));
    }

    [Fact]
    public void Render_SingleSection_HidesSectionLabel()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "Single", Grade = 2, Section = 1 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
        };

        vm.Render(assignment, courses);

        var item = Assert.Single(vm.Cells).Assignment;
        Assert.Equal("", item.SectionLabel);
        Assert.Equal("Single", item.TitleLabel);
    }

    [Fact]
    public void Render_DifferentSectionsInConsecutivePeriods_NotMerged()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1" },
            new() { Id = "X-02", Name = "전공", Grade = 2, Section = 2, ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-02", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        Assert.Contains(vm.Cells, c => c.Period == 1 && c.Assignment.CourseId == "X-01" && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Cells, c => c.Period == 2 && c.Assignment.CourseId == "X-02" && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void Render_UsesProfessorAndRoomNamesForDisplayLabels()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new()
            {
                Id = "X-01",
                Name = "전공",
                Grade = 2,
                Section = 1,
                ProfessorId = "P1",
                CoteachProfs = new List<string> { "P2" },
            },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "김교수" },
            new() { Id = "P2", Name = "박교수" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "공학관 101" },
            new() { Id = "R2", Name = "공학관 102" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 1, "R2"),
        };

        vm.Render(assignment, courses, professors, rooms);

        var item = Assert.Single(vm.Cells).Assignment;
        Assert.Equal("김교수", item.ProfessorLabel);
        Assert.Equal(new[] { "박교수" }, item.CoteachProfLabels);
        Assert.Equal("김교수, 박교수", item.ProfessorLine);
        Assert.Equal("공학관 101\n공학관 102", item.RoomsLabel);
    }

    [Fact]
    public void Render_DifferentSectionInsideExistingRowSpan_UsesSeparateSubColumn()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 2, ProfessorId = "P1" },
            new() { Id = "X-02", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
            new("X-02", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var rowSpanBlock = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "X-01");
        var otherSection = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "X-02");
        Assert.Equal(2, rowSpanBlock.Assignment.RowSpan);
        Assert.Equal(1, otherSection.Assignment.RowSpan);
        Assert.Equal(2, otherSection.Period);
        Assert.NotEqual(rowSpanBlock.SubColumnIdx, otherSection.SubColumnIdx);
        Assert.Equal(2, vm.DayGroups[0].Grades.Single(g => g.Grade == 2).Width);
    }

    [Fact]
    public void Render_SameCourseConsecutivePeriodsSameRoom_Merged()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var cell = Assert.Single(vm.Cells);
        Assert.Equal(1, cell.Period);
        Assert.Equal(2, cell.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_SameCourseTwoHourBlock_Merged()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 2 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var cell = Assert.Single(vm.Cells);
        Assert.Equal(1, cell.Period);
        Assert.Equal(2, cell.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_EmptyBlockStructureUsesHoursPerWeek()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", HoursPerWeek = 2 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var cell = Assert.Single(vm.Cells);
        Assert.Equal(2, cell.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_ThreeHourBlock_MergedAsOneSession()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", HoursPerWeek = 3, BlockStructure = new List<int> { 3 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
            new("X-01", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        var cell = Assert.Single(vm.Cells);
        Assert.Equal(1, cell.Period);
        Assert.Equal(3, cell.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_SectionMetadataDoesNotSplitThreeHourBlock()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 2, ProfessorId = "P1", HoursPerWeek = 3, BlockStructure = new List<int> { 3 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
            new("X-01", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        var cell = Assert.Single(vm.Cells);
        Assert.Equal(3, cell.Assignment.RowSpan);
        Assert.Equal("", cell.Assignment.SectionLabel);
    }

    [Fact]
    public void Render_StructuredMergeMode_SameCourseWithoutBlockEvidence_NotMerged()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 1, 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        Assert.Equal(2, vm.Cells.Count(c => c.Assignment.CourseId == "X-01"));
        Assert.All(vm.Cells.Where(c => c.Assignment.CourseId == "X-01"), c => Assert.Equal(1, c.Assignment.RowSpan));
    }

    [Fact]
    public void Render_StructuredMergeMode_RowSpanDoesNotHideDifferentSection()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 2 } },
            new() { Id = "X-02", Name = "전공", Grade = 2, Section = 2, ProfessorId = "P1", BlockStructure = new List<int> { 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
            new("X-02", 0, 2, "R1"),
        };

        vm.Render(assignment, courses);

        var rowSpanBlock = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "X-01");
        var otherSection = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "X-02");
        Assert.Equal(2, rowSpanBlock.Assignment.RowSpan);
        Assert.Equal(1, otherSection.Assignment.RowSpan);
        Assert.Equal(2, otherSection.Period);
        Assert.NotEqual(rowSpanBlock.SubColumnIdx, otherSection.SubColumnIdx);
        Assert.Equal(2, vm.DayGroups[0].Grades.Single(g => g.Grade == 2).Width);
    }

    [Fact]
    public void Render_StructuredMergeMode_OneHourAboveTwoHourBlock_KeepsOriginalRows()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "한시간", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 1 } },
            new() { Id = "B-02", Name = "두시간", Grade = 2, Section = 1, ProfessorId = "P2", BlockStructure = new List<int> { 2 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("A-01", 0, 1, "R1"),
            new("B-02", 0, 2, "R1"),
            new("B-02", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        var oneHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "A-01");
        var twoHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "B-02");
        Assert.Equal(1, oneHour.Period);
        Assert.Equal(1, oneHour.Assignment.RowSpan);
        Assert.Equal(2, twoHour.Period);
        Assert.Equal(2, twoHour.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_TwoHourBlockAboveOneHour_KeepsOriginalRows()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "한시간", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 1 } },
            new() { Id = "B-02", Name = "두시간", Grade = 2, Section = 1, ProfessorId = "P2", BlockStructure = new List<int> { 2 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("B-02", 0, 1, "R1"),
            new("B-02", 0, 2, "R1"),
            new("A-01", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        var twoHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "B-02");
        var oneHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "A-01");
        Assert.Equal(1, twoHour.Period);
        Assert.Equal(2, twoHour.Assignment.RowSpan);
        Assert.Equal(3, oneHour.Period);
        Assert.Equal(1, oneHour.Assignment.RowSpan);
    }

    [Fact]
    public void Render_StructuredMergeMode_OverlappingOneHourAndTwoHourBlock_UsesSeparateSubColumns()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "A-01", Name = "한시간", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 1 } },
            new() { Id = "B-02", Name = "두시간", Grade = 2, Section = 1, ProfessorId = "P2", BlockStructure = new List<int> { 2 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("A-01", 0, 2, "R1"),
            new("B-02", 0, 2, "R1"),
            new("B-02", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        var oneHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "A-01");
        var twoHour = Assert.Single(vm.Cells, c => c.Assignment.CourseId == "B-02");
        Assert.Equal(2, oneHour.Period);
        Assert.Equal(1, oneHour.Assignment.RowSpan);
        Assert.Equal(2, twoHour.Period);
        Assert.Equal(2, twoHour.Assignment.RowSpan);
        Assert.NotEqual(oneHour.SubColumnIdx, twoHour.SubColumnIdx);
    }

    [Fact]
    public void Render_StructuredMergeMode_DoesNotMergePastDeclaredBlockLength()
    {
        var vm = new UnifiedTimetableViewModel { MergeOnlyStructuredBlocks = true };
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1", BlockStructure = new List<int> { 2, 1 } },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R1"),
            new("X-01", 0, 3, "R1"),
        };

        vm.Render(assignment, courses);

        Assert.Contains(vm.Cells, c => c.Period == 1 && c.Assignment.RowSpan == 2);
        Assert.Contains(vm.Cells, c => c.Period == 3 && c.Assignment.RowSpan == 1);
    }

    [Fact]
    public void Render_SameCourseConsecutivePeriodsDifferentRooms_NotMerged()
    {
        var vm = new UnifiedTimetableViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "전공", Grade = 2, Section = 1, ProfessorId = "P1" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 2, "R2"),
        };

        vm.Render(assignment, courses);

        Assert.Contains(vm.Cells, c => c.Period == 1 && c.Assignment.RowSpan == 1);
        Assert.Contains(vm.Cells, c => c.Period == 2 && c.Assignment.RowSpan == 1);
    }
}
