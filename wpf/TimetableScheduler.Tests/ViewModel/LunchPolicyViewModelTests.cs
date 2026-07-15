using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Editors;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.ViewModel.Services;

namespace TimetableScheduler.Tests.ViewModel;

public class LunchPolicyViewModelTests
{
    private static readonly SchedulePolicy FlexiblePolicy = new()
    {
        LunchMode = LunchPolicyMode.BanOneOfPeriods4And5,
    };

    private static readonly IReadOnlyDictionary<int, int> MixedLunch =
        new Dictionary<int, int>
        {
            [0] = 4,
            [1] = 5,
            [2] = 4,
            [3] = 5,
            [4] = 4,
        };

    [Fact]
    public void TimeSlotPicker_FlexibleModeLeavesPeriods4And5Selectable()
    {
        var target = new List<TimeSlot>();
        var picker = new TimeSlotPickerViewModel(target, schedulePolicy: FlexiblePolicy);

        picker.Toggle(0, 4);
        picker.Toggle(0, 5);

        Assert.False(picker.CellAt(0, 4).IsLunch);
        Assert.False(picker.CellAt(0, 5).IsLunch);
        Assert.Contains(new TimeSlot(0, 4), target);
        Assert.Contains(new TimeSlot(0, 5), target);
    }

    [Theory]
    [InlineData(LunchPolicyMode.None, "1,2,3,4,5,6,7,8")]
    [InlineData(LunchPolicyMode.BanPeriod4, "1,2,5,6,7,8")]
    [InlineData(LunchPolicyMode.BanPeriod5, "1,2,3,6,7,8")]
    [InlineData(LunchPolicyMode.BanOneOfPeriods4And5, "1,2,3,5,6,7,8")]
    public void FixedTwoHourEditor_UsesLunchPolicyStartRestrictions(
        LunchPolicyMode mode,
        string expectedPeriodsCsv)
    {
        var entry = new BlockSlotEntry
        {
            BlockSize = 2,
            IsGraduate = false,
            SchedulePolicy = new SchedulePolicy { LunchMode = mode },
        };
        var expectedPeriods = expectedPeriodsCsv.Split(',').Select(int.Parse).ToArray();

        Assert.Equal(expectedPeriods, entry.PeriodOptions.Select(option => option.Period));
    }

    [Theory]
    [InlineData(LunchPolicyMode.BanPeriod4, 4)]
    [InlineData(LunchPolicyMode.BanPeriod5, 5)]
    public void TimeSlotPicker_StaticLunchModeDisablesOnlyThatPeriod(
        LunchPolicyMode mode,
        int lunchPeriod)
    {
        var target = new List<TimeSlot>();
        var picker = new TimeSlotPickerViewModel(
            target,
            schedulePolicy: new SchedulePolicy { LunchMode = mode });

        picker.Toggle(0, lunchPeriod);
        picker.Toggle(0, lunchPeriod == 4 ? 5 : 4);

        Assert.True(picker.CellAt(0, lunchPeriod).IsLunch);
        Assert.False(picker.CellAt(0, lunchPeriod).IsSelected);
        Assert.DoesNotContain(new TimeSlot(0, lunchPeriod), target);
        Assert.Contains(new TimeSlot(0, lunchPeriod == 4 ? 5 : 4), target);
    }

    [Fact]
    public void FixedEditor_CoercesExistingStartThatNoLongerMatchesLunchPolicy()
    {
        var section = new Course
        {
            Id = "C-01",
            Name = "C",
            Grade = 1,
            HoursPerWeek = 2,
            BlockStructure = new List<int> { 2 },
            IsFixed = true,
            FixedSlots = new List<TimeSlot> { new(0, 4), new(0, 5) },
        };
        var item = new CourseGroupItem
        {
            BaseId = "C",
            Sections = new List<Course> { section },
        };

        var editor = FixedSlotEditorViewModel.Build(item, isFixed: true, FlexiblePolicy);
        var entry = editor.SectionEditors.Single().BlockEntries.Single();

        Assert.DoesNotContain(4, entry.PeriodOptions.Select(option => option.Period));
        Assert.Equal(1, entry.SelectedPeriod);
        Assert.Equal(new List<TimeSlot> { new(0, 4), new(0, 5) }, section.FixedSlots);
    }

    [Fact]
    public void DataInputSelection_UpdatesPolicyBeforeGeneration()
    {
        var workspace = WorkspaceService.CreateSession(AppData.Empty());
        var viewModel = new DataInputViewModel(workspace, new SolverService());
        var ban4 = viewModel.LunchPolicyOptions.Single(option =>
            option.Mode == LunchPolicyMode.BanPeriod4);

        viewModel.SelectedLunchPolicy = ban4;

        Assert.Equal(LunchPolicyMode.BanPeriod4, workspace.SchedulePolicy.LunchMode);
        Assert.False(viewModel.IsSolveComplete);
        Assert.Contains("점심시간 조건이 변경", viewModel.StatusMessage);
        Assert.Contains(viewModel.LunchPolicyOptions, option =>
            option.Mode == LunchPolicyMode.None);
    }

    [Fact]
    public void TimetableGrid_UsesPerDayLunchCells_AndKeepsOpenCandidateVisible()
    {
        var courses = Courses();
        var assignments = Assignments();
        var grid = new TimetableGridViewModel();

        grid.Render(
            assignments,
            courses,
            schedulePolicy: FlexiblePolicy,
            lunchPeriodsByDay: MixedLunch);

        Assert.True(grid.CellAt(0, 4).IsLunch);
        Assert.False(grid.CellAt(0, 5).IsLunch);
        Assert.True(grid.CellAt(0, 5).IsOccupied);
        Assert.False(grid.CellAt(1, 4).IsLunch);
        Assert.True(grid.CellAt(1, 4).IsOccupied);
        Assert.True(grid.CellAt(1, 5).IsLunch);
    }

    [Fact]
    public void UnifiedGrid_UsesPerDayLunchCells_AndBuildsOpenPeriod5CourseCell()
    {
        var grid = new UnifiedTimetableViewModel { ExpandAllGrades = true };

        grid.Render(
            Assignments(),
            Courses(),
            schedulePolicy: FlexiblePolicy,
            lunchPeriodsByDay: MixedLunch);

        Assert.True(grid.IsLunch(0, 4));
        Assert.False(grid.IsLunch(0, 5));
        Assert.Contains(grid.Cells, cell =>
            cell.Day == 0 && cell.Period == 5 && cell.Assignment.CourseId == "A-01");
        Assert.False(grid.IsLunch(1, 4));
        Assert.True(grid.IsLunch(1, 5));
        Assert.Contains(grid.Cells, cell =>
            cell.Day == 1 && cell.Period == 4 && cell.Assignment.CourseId == "B-01");
    }

    [Fact]
    public void ResultsMiniPreview_MarksLunchPerCellInsteadOfWholeRow()
    {
        var snapshot = new AppData(
            Courses(),
            new List<Professor>(),
            new List<Room>(),
            new List<CrossGroup>(),
            new List<RetakeScenario>())
        {
            SchedulePolicy = FlexiblePolicy,
        };
        var workspace = WorkspaceService.CreateSession(snapshot);
        var viewModel = new ResultsViewModel(workspace);
        viewModel.SetSolutions(
            new[]
            {
                new RankedSolution(
                    Assignments(),
                    new SolutionScore(0, 0, 0, 0),
                    MixedLunch),
            },
            snapshot);

        var card = Assert.Single(viewModel.SolutionCards);
        var period4 = card.PreviewRows.Single(row => row.Period == 4);
        var period5 = card.PreviewRows.Single(row => row.Period == 5);

        Assert.False(period4.IsLunch);
        Assert.True(period4.Cells[0].IsLunch);
        Assert.False(period4.Cells[1].IsLunch);
        Assert.False(period5.IsLunch);
        Assert.False(period5.Cells[0].IsLunch);
        Assert.True(period5.Cells[1].IsLunch);
    }

    [Fact]
    public void ManualEdit_KeepsGeneratedLunchMapInGridAndExcelExport()
    {
        var snapshot = new AppData(
            Courses(),
            new List<Professor>(),
            new List<Room>
            {
                new Room { Id = "R1", Name = "R1" },
                new Room { Id = "R2", Name = "R2" },
            },
            new List<CrossGroup>(),
            new List<RetakeScenario>())
        {
            SchedulePolicy = FlexiblePolicy,
        };
        var workspace = WorkspaceService.CreateSession(snapshot);
        var viewModel = new ManualEditViewModel(workspace, new NullConflictDialogService());
        viewModel.LoadFromSnapshot(
            snapshot,
            new RankedSolution(
                Assignments(),
                new SolutionScore(0, 0, 0, 0),
                MixedLunch),
            "mixed lunch");
        var path = Path.Combine(Path.GetTempPath(), $"manual_lunch_{Guid.NewGuid():N}.xlsx");

        try
        {
            Assert.True(viewModel.Grid.IsLunch(0, 4));
            Assert.False(viewModel.Grid.IsLunch(0, 5));

            viewModel.ExportXlsxCommand.Execute(path);

            using var workbook = new ClosedXML.Excel.XLWorkbook(path);
            var sheet = workbook.Worksheet("통합 시간표");
            Assert.Equal("", sheet.Cell(9, 2).GetString());
            Assert.Contains("A", sheet.Cell(10, 2).GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ManualEdit_AllowsMoveIntoSavedLunchCell_WithoutShowingLunchViolation()
    {
        var snapshot = new AppData(
            Courses(),
            new List<Professor>(),
            new List<Room>
            {
                new Room { Id = "R1", Name = "R1" },
                new Room { Id = "R2", Name = "R2" },
            },
            new List<CrossGroup>(),
            new List<RetakeScenario>())
        {
            SchedulePolicy = FlexiblePolicy,
        };
        var viewModel = new ManualEditViewModel(
            WorkspaceService.CreateSession(snapshot),
            new NullConflictDialogService());
        viewModel.LoadFromSnapshot(
            snapshot,
            new RankedSolution(
                Assignments(),
                new SolutionScore(0, 0, 0, 0),
                MixedLunch),
            "mixed lunch");
        var source = viewModel.Grid.Cells.Single(cell =>
            cell.Day == 0 && cell.Period == 5 && cell.Assignment.CourseId == "A-01");

        var lunchTarget = viewModel.CanDropMove(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            targetDay: 0,
            targetPeriod: 4,
            targetGrade: source.Grade,
            targetSubColumnIdx: 0);
        var openTarget = viewModel.CanDropMove(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            targetDay: 1,
            targetPeriod: 4,
            targetGrade: source.Grade,
            targetSubColumnIdx: 0);

        Assert.True(lunchTarget);
        Assert.True(openTarget);

        var moved = viewModel.HandleDropMove(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            targetDay: 0,
            targetPeriod: 4,
            targetGrade: source.Grade,
            targetSubColumnIdx: 0);

        Assert.True(moved);
        Assert.DoesNotContain(viewModel.Conflicts, item =>
            item.Conflict.Type == ConflictType.LunchConflict);
        Assert.Empty(viewModel.Grid.ViolationLabels);
        Assert.True(viewModel.ValidateBeforeExport());
    }

    private static List<Course> Courses() =>
        new()
        {
            new Course { Id = "A-01", Name = "A", Grade = 1, HoursPerWeek = 1 },
            new Course { Id = "B-01", Name = "B", Grade = 2, HoursPerWeek = 1 },
        };

    private static List<SolutionAssignment> Assignments() =>
        new()
        {
            new SolutionAssignment("A-01", 0, 5, "R1"),
            new SolutionAssignment("B-01", 1, 4, "R2"),
        };
}
