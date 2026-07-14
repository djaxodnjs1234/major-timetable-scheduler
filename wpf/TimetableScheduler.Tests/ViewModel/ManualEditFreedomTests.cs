using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Grid;
using TimetableScheduler.ViewModel.Pages;
using TimetableScheduler.ViewModel.Services;

namespace TimetableScheduler.Tests.ViewModel;

public class ManualEditFreedomTests
{
    [Fact]
    public void Move_CanCreateProfessorAndRoomConflicts_AndBuildsBadgesAndConnectors()
    {
        var courses = new[]
        {
            Course("A-01", 1, "P1"),
            Course("B-01", 2, "P1"),
        };
        var viewModel = Create(
            courses,
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A"),
                new SolutionAssignment("B-01", 0, 2, "R1", "B"),
            });
        var source = Cell(viewModel, "B-01");

        var moved = viewModel.HandleDropMove(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            0,
            1,
            source.Grade,
            0);

        Assert.True(moved);
        Assert.Contains(viewModel.Conflicts, item => item.Conflict.Type == ConflictType.ProfessorConflict);
        Assert.Contains(viewModel.Conflicts, item => item.Conflict.Type == ConflictType.RoomConflict);
        Assert.Contains(viewModel.Grid.ViolationLabels.Values, label => label.Contains("교수 중복"));
        Assert.Contains(viewModel.Grid.ViolationLabels.Values, label => label.Contains("강의실 중복"));
        Assert.All(viewModel.ConflictGroups, item => Assert.DoesNotContain("ME-", item.DisplayTitle));
        Assert.Contains(viewModel.ConflictGroups, item =>
            item.DisplayTitle.StartsWith("월 1교시 /", StringComparison.Ordinal));
        Assert.Contains(viewModel.ConflictGroups, item =>
            item.DetailConflicts.Any(conflict => conflict.Type == ConflictType.ProfessorConflict));
        Assert.Contains(viewModel.ConflictGroups, item =>
            item.DetailConflicts.Any(conflict => conflict.Type == ConflictType.RoomConflict));
        Assert.Contains(viewModel.Grid.ConflictConnectors, connector => connector.Label == "교수 중복");
        Assert.Contains(viewModel.Grid.ConflictConnectors, connector => connector.Label == "강의실 중복");
    }

    [Fact]
    public void RoomChange_CanCreateRoomConflictWithoutConfirmation()
    {
        var courses = new[]
        {
            Course("A-01", 1, "P1"),
            Course("B-01", 2, "P2"),
        };
        var viewModel = Create(
            courses,
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A"),
                new SolutionAssignment("B-01", 0, 1, "R2", "B"),
            });
        var source = Cell(viewModel, "B-01");
        viewModel.SelectCell(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment);

        viewModel.NewRoomId = "R1";
        viewModel.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R1" }, viewModel.SelectedAssignment!.Rooms);
        Assert.Contains(viewModel.Conflicts, item => item.Conflict.Type == ConflictType.RoomConflict);
    }

    [Fact]
    public void RoomChange_CanUseCourseBlockedRoom_WithoutManualEditViolation()
    {
        var course = Course("A-01", 1, "P1");
        course.UnavailableRooms = new List<string> { "R2" };
        var dialog = new CapturingConflictDialogService();
        var viewModel = Create(
            new[] { course },
            new[] { new SolutionAssignment("A-01", 0, 1, "R1", "A") },
            dialog: dialog);
        var source = Cell(viewModel, "A-01");
        viewModel.SelectCell(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment);

        viewModel.NewRoomId = "R2";
        viewModel.ApplyRoomChangeCommand.Execute(null);

        Assert.Equal(new[] { "R2" }, viewModel.SelectedAssignment!.Rooms);
        Assert.DoesNotContain(viewModel.Conflicts, item =>
            item.Conflict.Type == ConflictType.CourseUnavailableRoomViolation);
        Assert.DoesNotContain(viewModel.Grid.ViolationLabels.Values, label =>
            label.Contains("과목 불가강의실", StringComparison.Ordinal));
        Assert.True(viewModel.ValidateBeforeExport());

        viewModel.ValidateAllCommand.Execute(null);

        var checks = Assert.Single(dialog.ValidationCheckCalls);
        Assert.DoesNotContain(
            checks.SelectMany(check => check.DetailConflicts),
            conflict => conflict.Type == ConflictType.CourseUnavailableRoomViolation);
    }

    [Fact]
    public void FixedCourse_CanMove_AndShowsOriginalFixedSlotAsWarning()
    {
        var fixedCourse = Course("A-01", 1, "P1");
        fixedCourse.IsFixed = true;
        fixedCourse.FixedSlots = new List<TimeSlot> { new(0, 1) };
        var dialog = new CapturingConflictDialogService();
        var viewModel = Create(
            new[] { fixedCourse },
            new[] { new SolutionAssignment("A-01", 0, 1, "R1", "A") },
            dialog: dialog);
        var source = Cell(viewModel, "A-01");

        var moved = viewModel.HandleDropMove(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment,
            0,
            2,
            source.Grade,
            0);

        Assert.True(moved);
        var panelItem = Assert.Single(viewModel.ConflictGroups.Where(item =>
            item.Type == ConflictType.FixedTimeViolation));
        var panelConflict = Assert.Single(panelItem.DetailConflicts);
        Assert.Equal(ConflictSeverity.Warning, panelConflict.Severity);
        Assert.Contains("원래 고정시간", panelConflict.Description);
        Assert.Contains("월 1교시", panelConflict.Description);
        Assert.Contains("현재 위치: 월 2교시", panelConflict.Description);
        Assert.Contains(viewModel.Grid.ViolationLabels.Values, label =>
            label.Contains("고정시간 이탈", StringComparison.Ordinal));
        Assert.True(viewModel.ValidateBeforeExport());

        viewModel.ValidateAllCommand.Execute(null);

        var checks = Assert.Single(dialog.ValidationCheckCalls);
        Assert.DoesNotContain(checks, check => check.Name == "고정 시간 위반");
        Assert.DoesNotContain(
            checks.SelectMany(check => check.DetailConflicts),
            conflict => conflict.Type == ConflictType.FixedTimeViolation);
    }

    [Fact]
    public void GraduateDaytimePlacement_IsExcludedFromManualEditValidation()
    {
        var graduateCourse = Course("G-01", AcademicLevels.GraduateGrade, "P1");
        var dialog = new CapturingConflictDialogService();
        var viewModel = Create(
            new[] { graduateCourse },
            new[] { new SolutionAssignment("G-01", 0, 1, "R1", "G") },
            dialog: dialog);

        Assert.DoesNotContain(viewModel.Conflicts, item =>
            item.Conflict.Type == ConflictType.AcademicLevelTimeBandViolation);
        Assert.Empty(viewModel.Grid.ViolationLabels);
        Assert.True(viewModel.ValidateBeforeExport());

        viewModel.ValidateAllCommand.Execute(null);

        var checks = Assert.Single(dialog.ValidationCheckCalls);
        Assert.DoesNotContain(
            checks.SelectMany(check => check.DetailConflicts),
            conflict => conflict.Type == ConflictType.AcademicLevelTimeBandViolation);
    }

    [Fact]
    public void Cross_RemainsAvailableWhenItCreatesProfessorAndRoomConflicts()
    {
        var courses = new[]
        {
            Course("A-01", 1, "P1"),
            Course("B-01", 1, "P1"),
        };
        var viewModel = Create(
            courses,
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A"),
                new SolutionAssignment("B-01", 0, 2, "R1", "B"),
            });
        var source = Cell(viewModel, "A-01");
        var target = Cell(viewModel, "B-01");
        viewModel.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var hover = viewModel.EvaluateCrossHover(
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);
        viewModel.HandleCrossAddRequested(
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);

        Assert.True(hover.CanCreate, hover.Reason);
        Assert.True(viewModel.WorkingCrossLinks.Count == 1, viewModel.StatusMessage);
        Assert.Contains(viewModel.Conflicts, item => item.Conflict.Type == ConflictType.ProfessorConflict);
        Assert.Contains(viewModel.Conflicts, item => item.Conflict.Type == ConflictType.RoomConflict);
    }

    [Fact]
    public void Swap_IsAvailableAcrossGradesAndForFixedCourse()
    {
        var fixedCourse = Course("A-01", 1, "P1");
        fixedCourse.IsFixed = true;
        fixedCourse.FixedSlots = new List<TimeSlot> { new(0, 1) };
        var viewModel = Create(
            new[] { fixedCourse, Course("B-01", 2, "P2") },
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A"),
                new SolutionAssignment("B-01", 0, 3, "R2", "B"),
            });
        var source = Cell(viewModel, "A-01");
        var target = Cell(viewModel, "B-01");
        viewModel.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var hover = viewModel.EvaluateSwapHover(
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);
        viewModel.HandleSwapRequested(
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);

        Assert.True(hover.CanSwap, hover.Reason);
        Assert.Contains(viewModel.Grid.Cells, cell =>
            cell.Assignment.CourseId == "A-01" && cell.Period == 3);
        Assert.Contains(viewModel.Conflicts, item =>
            item.Conflict.Type == ConflictType.FixedTimeViolation
            && item.Conflict.Severity == ConflictSeverity.Warning);
    }

    [Fact]
    public void Swap_IsAvailableWhenTargetBlockIsCrossLinked()
    {
        var viewModel = Create(
            new[]
            {
                Course("A-01", 1, "P1"),
                Course("B-01", 1, "P2"),
                Course("C-01", 1, "P3"),
            },
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A"),
                new SolutionAssignment("B-01", 1, 1, "R2", "B"),
                new SolutionAssignment("C-01", 1, 1, "R3", "C"),
            });
        var crossLeft = Cell(viewModel, "B-01");
        var crossRight = Cell(viewModel, "C-01");
        viewModel.SelectCell(
            crossLeft.Day,
            crossLeft.Period,
            crossLeft.Grade,
            crossLeft.SubColumnIdx,
            crossLeft.Assignment);
        viewModel.HandleCrossAddRequested(
            crossRight.Day,
            crossRight.Period,
            crossRight.Grade,
            crossRight.SubColumnIdx,
            crossRight.Assignment);
        Assert.Single(viewModel.WorkingCrossLinks);

        var source = Cell(viewModel, "A-01");
        var target = Cell(viewModel, "B-01");
        viewModel.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var hover = viewModel.EvaluateSwapHover(
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);
        viewModel.HandleSwapRequested(
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);

        Assert.True(hover.CanSwap, hover.Reason);
        Assert.Contains(viewModel.Grid.Cells, cell =>
            cell.Assignment.CourseId == "A-01" && cell.Day == 1 && cell.Period == 1);
        Assert.Contains(viewModel.Grid.Cells, cell =>
            cell.Assignment.CourseId == "B-01" && cell.Day == 0 && cell.Period == 1);
    }

    [Fact]
    public void Cross_SameCourseTwoHourAndOneHourBlocksRemainSeparate_AndCanSwap()
    {
        var dataStructures = Course("DS-01", 1, "P1");
        dataStructures.Name = "자료구조";
        dataStructures.HoursPerWeek = 3;
        dataStructures.BlockStructure = new List<int> { 2, 1 };
        var viewModel = Create(
            new[] { dataStructures, Course("OTHER-01", 1, "P2") },
            new[]
            {
                new SolutionAssignment("DS-01", 0, 1, "R1"),
                new SolutionAssignment("DS-01", 0, 2, "R1"),
                new SolutionAssignment("DS-01", 0, 4, "R1"),
                new SolutionAssignment("OTHER-01", 0, 7, "R2", "OTHER"),
            },
            new SchedulePolicy { LunchMode = LunchPolicyMode.None });
        var twoHour = CellAt(viewModel, "DS-01", 0, 1);
        var oneHour = CellAt(viewModel, "DS-01", 0, 4);
        viewModel.SelectCell(
            oneHour.Day,
            oneHour.Period,
            oneHour.Grade,
            oneHour.SubColumnIdx,
            oneHour.Assignment);

        var crossHover = viewModel.EvaluateCrossHover(
            twoHour.Day,
            twoHour.Period,
            twoHour.Grade,
            twoHour.SubColumnIdx,
            twoHour.Assignment);
        viewModel.HandleCrossAddRequested(
            twoHour.Day,
            twoHour.Period,
            twoHour.Grade,
            twoHour.SubColumnIdx,
            twoHour.Assignment);

        Assert.True(crossHover.CanCreate, crossHover.Reason);
        Assert.Single(viewModel.WorkingCrossLinks);
        var crossedDataStructureCells = viewModel.Grid.Cells
            .Where(cell => cell.Assignment.CourseId == "DS-01" && cell.Day == 0 && cell.Period == 1)
            .ToList();
        Assert.Contains(crossedDataStructureCells, cell => cell.Assignment.RowSpan == 2);
        Assert.Contains(crossedDataStructureCells, cell =>
            cell.Assignment.RowSpan == 1
            && CellAssignment.IsManualVisualOccurrenceAssignmentId(cell.Assignment.AssignmentId));

        var source = Cell(viewModel, "OTHER-01");
        var target = crossedDataStructureCells.Single(cell => cell.Assignment.RowSpan == 1);
        viewModel.SelectCell(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment);

        var swapHover = viewModel.EvaluateSwapHover(
            target.Day,
            target.Period,
            target.Grade,
            target.SubColumnIdx,
            target.Assignment);

        Assert.True(swapHover.CanSwap, swapHover.Reason);
    }

    [Fact]
    public void TwoHourMove_FallsBackToVisualBlockRowsWhenAssignmentIdMatchesOnlyOneRow()
    {
        var twoHour = Course("A-01", 1, "P1");
        twoHour.HoursPerWeek = 2;
        twoHour.BlockStructure = new List<int> { 2 };
        var viewModel = Create(
            new[] { twoHour, Course("B-01", 1, "P2") },
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A-start"),
                new SolutionAssignment("A-01", 0, 2, "R1", "A-covered"),
                new SolutionAssignment("B-01", 0, 6, "R2", "B"),
            },
            new SchedulePolicy { LunchMode = LunchPolicyMode.None });
        SetWorkingAssignments(
            viewModel,
            new SolutionAssignment("A-01", 0, 1, "R1", "A-start"),
            new SolutionAssignment("A-01", 0, 2, "R1", "A-covered"),
            new SolutionAssignment("B-01", 0, 6, "R2", "B"));
        var source = CellAssignment.FromCourse(
            twoHour,
            new[] { "R1" },
            rowSpan: 2,
            assignmentId: "A-start");

        var moved = viewModel.HandleDropMove(
            0,
            1,
            1,
            0,
            source,
            0,
            3,
            1,
            0);

        Assert.True(moved, viewModel.StatusMessage);
        Assert.Contains(viewModel.Grid.Cells, cell =>
            cell.Assignment.CourseId == "A-01" && cell.Day == 0 && cell.Period == 3 && cell.Assignment.RowSpan == 2);
        Assert.DoesNotContain(viewModel.Grid.Cells, cell =>
            cell.Assignment.CourseId == "A-01" && cell.Day == 0 && cell.Period == 1);
    }

    [Theory]
    [InlineData(2, 13)]
    [InlineData(3, 12)]
    public void MultiHourMoveStates_ShowBlockedStartsInsteadOfHidingThem(
        int rowSpan,
        int outOfRangeStart)
    {
        var sourceCourse = Course("A-01", 1, "P1");
        sourceCourse.HoursPerWeek = rowSpan;
        sourceCourse.BlockStructure = new List<int> { rowSpan };
        var assignments = Enumerable.Range(1, rowSpan)
            .Select(period => new SolutionAssignment("A-01", 0, period, "R1", "A"))
            .Append(new SolutionAssignment("B-01", 0, 6, "R2", "B"))
            .ToArray();
        var viewModel = Create(
            new[] { sourceCourse, Course("B-01", 1, "P2") },
            assignments,
            new SchedulePolicy { LunchMode = LunchPolicyMode.None });
        var source = Cell(viewModel, "A-01");

        viewModel.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        Assert.Equal(
            ManualMoveCellState.Blocked,
            viewModel.Grid.EditStates[new UnifiedCellKey(0, 5, 1, 0)].State);
        Assert.Equal(
            ManualMoveCellState.Blocked,
            viewModel.Grid.EditStates[new UnifiedCellKey(0, outOfRangeStart, 1, 0)].State);
    }

    [Fact]
    public void ManualBlockCommands_AddUpdateAndDeleteStagedCourseBlock()
    {
        var viewModel = Create(
            new[] { Course("A-01", 1, "P1") },
            Array.Empty<SolutionAssignment>());

        viewModel.NewBlockKind = ManualEditViewModel.ManualBlockKind.Course;
        viewModel.NewBlockCourseId = "A-01";
        viewModel.NewBlockRoomId = "R1";
        viewModel.NewBlockRowSpan = 2;
        viewModel.AddManualBlockToStagingCommand.Execute(null);

        var added = Assert.Single(viewModel.StagedBlocks);
        Assert.Equal("A-01", added.CourseId);
        Assert.Equal(2, added.RowSpan);
        Assert.Equal(2, added.Assignments.Count);

        viewModel.NewBlockRoomId = "R2";
        viewModel.NewBlockRowSpan = 1;
        viewModel.UpdateSelectedStagedBlockCommand.Execute(null);

        var updated = Assert.Single(viewModel.StagedBlocks);
        Assert.Equal("R2", Assert.Single(updated.Assignments).RoomId);
        Assert.Equal(1, updated.RowSpan);

        viewModel.DeleteSelectedStagedBlockCommand.Execute(null);

        Assert.Empty(viewModel.StagedBlocks);
    }

    [Fact]
    public void ManualBlockAdd_UsesSimpleTextInputsGradeAndHours()
    {
        var viewModel = Create(
            Array.Empty<Course>(),
            Array.Empty<SolutionAssignment>());

        viewModel.NewBlockCourseName = "임시특강";
        viewModel.NewBlockProfessorName = "홍교수";
        viewModel.NewBlockGrade = 3;
        viewModel.NewBlockRowSpan = 2;
        viewModel.NewBlockRoomName = "새강의실";
        viewModel.AddManualBlockToStagingCommand.Execute(null);

        var staged = Assert.Single(viewModel.StagedBlocks);
        Assert.Equal("임시특강 A분반", staged.Title);
        Assert.Equal(3, staged.Grade);
        Assert.Equal(2, staged.RowSpan);
        Assert.Equal(2, staged.Assignments.Count);

        viewModel.SelectedStagedBlock = staged;
        var placed = viewModel.HandleStagedBlockDrop(0, 1, 3, 0);

        Assert.True(placed, viewModel.StatusMessage);
        Assert.Empty(viewModel.StagedBlocks);
        var cell = Assert.Single(viewModel.Grid.Cells.Where(cell =>
            cell.Day == 0
            && cell.Period == 1
            && cell.Grade == 3
            && cell.Assignment.RowSpan == 2
            && cell.Assignment.CourseName == "임시특강"));
        Assert.Equal("홍교수", cell.Assignment.ProfessorDisplayName);
        Assert.Contains("새강의실", cell.Assignment.RoomDisplayNames);
    }

    [Fact]
    public void ManualBlockAdd_AdvancedStructureAndSectionCount_CreateMultipleStagedBlocks()
    {
        var viewModel = Create(
            Array.Empty<Course>(),
            Array.Empty<SolutionAssignment>());

        viewModel.NewBlockCourseName = "자료구조";
        viewModel.NewBlockProfessorName = "홍교수";
        viewModel.NewBlockGrade = 2;
        viewModel.NewBlockRowSpan = 3;
        viewModel.NewBlockRoomName = "새강의실";
        Assert.False(viewModel.IsNewBlockStructureEnabled);
        Assert.Contains("2,1", viewModel.NewBlockStructureOptions);
        viewModel.IsNewBlockStructureEnabled = true;
        viewModel.NewBlockStructureText = "2,1";
        viewModel.NewBlockSectionCount = 2;
        viewModel.AddManualBlockToStagingCommand.Execute(null);

        Assert.Equal(4, viewModel.StagedBlocks.Count);
        Assert.Equal(new[] { 1, 1, 2, 2 }, viewModel.StagedBlocks.Select(block => block.RowSpan).OrderBy(value => value));
        Assert.Equal(2, viewModel.StagedBlocks.Count(block => block.Title == "자료구조 A분반"));
        Assert.Equal(2, viewModel.StagedBlocks.Count(block => block.Title == "자료구조 B분반"));
        Assert.All(viewModel.StagedBlocks, block =>
        {
            Assert.Equal(2, block.Grade);
            Assert.Equal("새강의실", Assert.Single(block.Assignment.RoomDisplayNames));
        });
    }

    [Fact]
    public void ManualBlockAdd_BlockStructureDefaultsOff_SectionCountMaxNine_AndRejectsCourseNameSpaces()
    {
        var viewModel = Create(
            Array.Empty<Course>(),
            Array.Empty<SolutionAssignment>());

        viewModel.NewBlockCourseName = "자료 구조";
        viewModel.NewBlockProfessorName = "홍교수";
        viewModel.NewBlockGrade = 2;
        viewModel.NewBlockRowSpan = 3;
        viewModel.NewBlockRoomName = "새강의실";
        viewModel.NewBlockStructureText = "2,1";
        viewModel.NewBlockSectionCount = 15;

        Assert.Equal(9, viewModel.NewBlockSectionCount);

        viewModel.AddManualBlockToStagingCommand.Execute(null);

        Assert.Empty(viewModel.StagedBlocks);
        Assert.Contains("띄어쓰기", viewModel.StatusMessage);

        viewModel.NewBlockCourseName = "자료구조";
        viewModel.AddManualBlockToStagingCommand.Execute(null);

        Assert.Equal(9, viewModel.StagedBlocks.Count);
        Assert.All(viewModel.StagedBlocks, block => Assert.Equal(3, block.RowSpan));
    }

    [Fact]
    public void Inspector_CanEditProfessorCoteachAndRooms_KeepingMinimumRoom()
    {
        var viewModel = Create(
            new[] { Course("A-01", 1, "P1") },
            new[] { new SolutionAssignment("A-01", 0, 1, "R1", "A") });
        var source = Cell(viewModel, "A-01");
        viewModel.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        var roomOption = Assert.Single(viewModel.AddableRoomOptions.Where(option => option.RoomId == "R2"));
        Assert.Equal("R2\t일반\t-", roomOption.DisplayText);

        viewModel.SelectedPrimaryProfessorId = "P2";
        viewModel.ApplyPrimaryProfessorChangeCommand.Execute(null);

        Assert.Equal("P2", viewModel.SelectedAssignment!.ProfessorId);

        viewModel.SelectedCoteachProfessorId = "P3";
        viewModel.AddCoteachProfessorCommand.Execute(null);

        Assert.Contains("P3", viewModel.SelectedAssignment!.CoteachProfIds);

        viewModel.RemoveCoteachProfessorCommand.Execute("P3");

        Assert.DoesNotContain("P3", viewModel.SelectedAssignment!.CoteachProfIds);

        viewModel.SelectedRoomToAddId = "R2";
        viewModel.AddSelectedRoomToAssignmentCommand.Execute(null);

        Assert.Equal(new[] { "R1", "R2" }, viewModel.SelectedAssignment!.Rooms.OrderBy(id => id).ToArray());

        viewModel.RemoveAssignedRoomCommand.Execute("R1");

        Assert.Equal(new[] { "R2" }, viewModel.SelectedAssignment!.Rooms);

        viewModel.RemoveAssignedRoomCommand.Execute("R2");

        Assert.Equal(new[] { "R2" }, viewModel.SelectedAssignment!.Rooms);
        Assert.Contains("최소 1개", viewModel.StatusMessage);
    }

    [Fact]
    public void StagedBlockSelection_PopulatesInspectorAndAllowsInspectorEdits()
    {
        var viewModel = Create(
            Array.Empty<Course>(),
            Array.Empty<SolutionAssignment>());

        viewModel.NewBlockCourseName = "임시특강";
        viewModel.NewBlockProfessorName = "P1";
        viewModel.NewBlockGrade = 2;
        viewModel.NewBlockRowSpan = 1;
        viewModel.NewBlockRoomName = "R1";
        viewModel.AddManualBlockToStagingCommand.Execute(null);

        viewModel.SelectedStagedBlock = Assert.Single(viewModel.StagedBlocks);

        Assert.Null(viewModel.SelectedAssignment);
        Assert.True(viewModel.HasInspectorSelection);
        Assert.False(viewModel.HasSelectedCoteachProfessors);
        Assert.True(viewModel.HasNoSelectedCoteachProfessors);
        Assert.Equal("임시특강", viewModel.InspectorAssignment!.CourseName);
        Assert.Contains("보관함", viewModel.SelectedSlotDisplayText);

        viewModel.SelectedCoteachProfessorId = "P2";
        viewModel.AddCoteachProfessorCommand.Execute(null);

        Assert.Contains("P2", viewModel.InspectorAssignment!.CoteachProfIds);
        Assert.True(viewModel.HasSelectedCoteachProfessors);

        viewModel.SelectedRoomToAddId = "R2";
        viewModel.AddSelectedRoomToAssignmentCommand.Execute(null);

        Assert.Equal(new[] { "R1", "R2" }, viewModel.InspectorAssignment!.Rooms.OrderBy(id => id).ToArray());

        viewModel.RemoveAssignedRoomCommand.Execute("R1");

        Assert.Equal(new[] { "R2" }, viewModel.InspectorAssignment!.Rooms);

        viewModel.DeleteSelectedBlockCommand.Execute(null);

        Assert.Empty(viewModel.StagedBlocks);
        Assert.False(viewModel.HasInspectorSelection);
    }

    [Fact]
    public void InspectorProfessorOptions_DeduplicatesSameDisplayName_AndKeepsConflictIdentity()
    {
        var viewModel = Create(
            new[]
            {
                Course("A-01", 1, "P1"),
                Course("B-01", 1, "P1"),
            },
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A"),
                new SolutionAssignment("B-01", 0, 1, "R2", "B"),
            },
            professors: new[]
            {
                new Professor { Id = "P1", Name = "김교수" },
                new Professor { Id = "P1-DUP", Name = "김교수" },
                new Professor { Id = "P2", Name = "이교수" },
            });
        var source = Cell(viewModel, "A-01");
        viewModel.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        var kim = Assert.Single(viewModel.ProfessorOptions.Where(option => option.DisplayName == "김교수"));
        Assert.Equal("P1", kim.ProfessorId);
        Assert.DoesNotContain(viewModel.AddableCoteachProfessorOptions, option => option.DisplayName == "김교수");

        viewModel.SelectedPrimaryProfessorId = kim.ProfessorId;
        viewModel.ApplyPrimaryProfessorChangeCommand.Execute(null);

        Assert.Contains(viewModel.Conflicts, item => item.Conflict.Type == ConflictType.ProfessorConflict);
    }

    [Fact]
    public void InspectorDelete_RemovesOnlySelectedBlock()
    {
        var viewModel = Create(
            new[] { Course("A-01", 1, "P1") },
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A"),
                new SolutionAssignment("A-01", 1, 1, "R1", "B"),
            });
        var source = CellAt(viewModel, "A-01", 0, 1);
        viewModel.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);

        viewModel.DeleteSelectedBlockCommand.Execute(null);

        Assert.DoesNotContain(viewModel.Grid.Cells, cell =>
            cell.Assignment.AssignmentId == "A");
        Assert.Contains(viewModel.Grid.Cells, cell =>
            cell.Assignment.AssignmentId == "B"
            && cell.Day == 1
            && cell.Period == 1);
        Assert.Null(viewModel.SelectedAssignment);
    }

    [Fact]
    public void InspectorDelete_WithWholeCourseChecked_RemovesPlacedAndStagedCourseBlocks()
    {
        var dataA = Course("DATA-A", 1, "P1");
        dataA.Name = "자료구조";
        dataA.Section = 1;
        var dataB = Course("DATA-B", 1, "P2");
        dataB.Name = "자료구조";
        dataB.Section = 2;
        var operatingSystems = Course("OS-01", 1, "P3");
        operatingSystems.Name = "운영체제";
        var viewModel = Create(
            new[] { dataA, dataB, operatingSystems },
            new[]
            {
                new SolutionAssignment("DATA-A", 0, 1, "R1", "A1"),
                new SolutionAssignment("DATA-B", 1, 3, "R1", "B1"),
                new SolutionAssignment("OS-01", 0, 1, "R2", "OS1"),
            });
        viewModel.NewBlockKind = ManualEditViewModel.ManualBlockKind.Course;
        viewModel.NewBlockCourseId = "DATA-B";
        viewModel.NewBlockRoomId = "R3";
        viewModel.NewBlockRowSpan = 1;
        viewModel.AddManualBlockToStagingCommand.Execute(null);

        var source = CellAt(viewModel, "DATA-A", 0, 1);
        viewModel.SelectCell(source.Day, source.Period, source.Grade, source.SubColumnIdx, source.Assignment);
        Assert.False(viewModel.DeleteEntireSelectedCourse);

        viewModel.DeleteEntireSelectedCourse = true;
        viewModel.DeleteSelectedBlockCommand.Execute(null);

        Assert.DoesNotContain(viewModel.Grid.Cells, cell => cell.Assignment.CourseName == "자료구조");
        Assert.DoesNotContain(viewModel.StagedBlocks, block => block.Assignment.CourseName == "자료구조");
        Assert.Contains(viewModel.Grid.Cells, cell => cell.Assignment.CourseId == "OS-01");
        Assert.Null(viewModel.SelectedAssignment);
        Assert.False(viewModel.DeleteEntireSelectedCourse);
    }

    [Fact]
    public void InspectorDelete_WithWholeCourseChecked_ForStagedSelection_RemovesMatchingStagedBlocksOnly()
    {
        var dataA = Course("DATA-A", 1, "P1");
        dataA.Name = "자료구조";
        dataA.Section = 1;
        var dataB = Course("DATA-B", 1, "P2");
        dataB.Name = "자료구조";
        dataB.Section = 2;
        var operatingSystems = Course("OS-01", 1, "P3");
        operatingSystems.Name = "운영체제";
        var viewModel = Create(
            new[] { dataA, dataB, operatingSystems },
            Array.Empty<SolutionAssignment>());
        viewModel.NewBlockKind = ManualEditViewModel.ManualBlockKind.Course;
        viewModel.NewBlockCourseId = "DATA-A";
        viewModel.NewBlockRoomId = "R1";
        viewModel.NewBlockRowSpan = 1;
        viewModel.AddManualBlockToStagingCommand.Execute(null);
        viewModel.NewBlockCourseId = "DATA-B";
        viewModel.AddManualBlockToStagingCommand.Execute(null);
        viewModel.NewBlockCourseId = "OS-01";
        viewModel.NewBlockRoomId = "R2";
        viewModel.AddManualBlockToStagingCommand.Execute(null);

        viewModel.SelectedStagedBlock = viewModel.StagedBlocks.First(block => block.CourseId == "DATA-A");
        viewModel.DeleteEntireSelectedCourse = true;
        viewModel.DeleteSelectedBlockCommand.Execute(null);

        var remaining = Assert.Single(viewModel.StagedBlocks);
        Assert.Equal("OS-01", remaining.CourseId);
        Assert.False(viewModel.HasInspectorSelection);
        Assert.False(viewModel.DeleteEntireSelectedCourse);
    }

    [Fact]
    public void ManualBlockCommands_AddGradeFixedBlock_AndPlaceItOnTimetable()
    {
        var viewModel = Create(
            new[] { Course("A-01", 1, "P1") },
            Array.Empty<SolutionAssignment>());

        viewModel.NewBlockKind = ManualEditViewModel.ManualBlockKind.GradeFixed;
        viewModel.NewBlockGrade = 2;
        viewModel.NewBlockRowSpan = 1;
        viewModel.AddManualBlockToStagingCommand.Execute(null);

        viewModel.SelectedStagedBlock = Assert.Single(viewModel.StagedBlocks);
        var placed = viewModel.HandleStagedBlockDrop(0, 3, 2, 0);

        Assert.True(placed, viewModel.StatusMessage);
        Assert.Empty(viewModel.StagedBlocks);
        Assert.Contains(viewModel.Grid.Cells, cell =>
            cell.Day == 0
            && cell.Period == 3
            && cell.Grade == 2
            && cell.Assignment.IsSchoolFixed
            && cell.Assignment.SchoolFixedTargetGrade == 2);
    }

    [Fact]
    public void DeleteSchoolFixedBlock_DoesNotMutateSourcePreviewSnapshot()
    {
        var fixedBlock = Course("FIX-01", 1, "P1");
        fixedBlock.Name = "Fixed";
        fixedBlock.IsFixed = true;
        fixedBlock.IsSchoolFixed = true;
        fixedBlock.SchoolFixedTargetGrade = SchoolFixedTimePolicy.AllGrades;
        fixedBlock.FixedSlots = new List<TimeSlot> { new(0, 1) };
        fixedBlock.BlockStructure = new List<int> { 1 };
        var snapshot = new AppData(
            new List<Course> { fixedBlock },
            new List<Professor> { new() { Id = "P1", Name = "P1" } },
            new List<Room> { new() { Id = "R1", Name = "R1" } },
            new List<CrossGroup>(),
            new List<RetakeScenario>());
        var lunchPeriods = SchedulePolicyRules.StaticLunchPeriodsByDay(SchedulePolicy.Default, Constants.Days);
        var viewModel = new ManualEditViewModel(
            WorkspaceService.CreateSession(snapshot),
            new NullConflictDialogService());
        viewModel.LoadFromSnapshot(
            snapshot,
            new RankedSolution(
                Array.Empty<SolutionAssignment>(),
                new SolutionScore(0, 0, 0, 0),
                lunchPeriods),
            "manual freedom");
        var source = viewModel.Grid.Cells.First(cell =>
            cell.Assignment.IsSchoolFixed
            && cell.Day == 0
            && cell.Period == 1
            && cell.Grade == 1);

        viewModel.SelectCell(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment);
        viewModel.DeleteSelectedBlockCommand.Execute(null);

        Assert.DoesNotContain(viewModel.Grid.Cells, cell =>
            cell.Assignment.IsSchoolFixed
            && cell.Day == 0
            && cell.Period == 1);
        Assert.Equal(new[] { new TimeSlot(0, 1) }, snapshot.Courses.Single().FixedSlots);
    }

    [Fact]
    public void StageSelectedBlock_AllowsDisplayedGradeFixedBlock_ToMoveAsManualBlock()
    {
        var fixedBlock = Course("FIX-01", 1, "P1");
        fixedBlock.Name = "Fixed";
        fixedBlock.IsFixed = true;
        fixedBlock.IsSchoolFixed = true;
        fixedBlock.SchoolFixedTargetGrade = 1;
        fixedBlock.FixedSlots = new List<TimeSlot> { new(0, 1) };
        fixedBlock.BlockStructure = new List<int> { 1 };
        var viewModel = Create(
            new[] { fixedBlock },
            Array.Empty<SolutionAssignment>());
        var source = viewModel.Grid.Cells.Single(cell =>
            cell.Assignment.IsSchoolFixed
            && cell.Day == 0
            && cell.Period == 1
            && cell.Grade == 1);

        viewModel.SelectCell(
            source.Day,
            source.Period,
            source.Grade,
            source.SubColumnIdx,
            source.Assignment);
        viewModel.StageSelectedBlockCommand.Execute(null);

        viewModel.SelectedStagedBlock = Assert.Single(viewModel.StagedBlocks);
        Assert.DoesNotContain(viewModel.Grid.Cells, cell =>
            cell.Assignment.IsSchoolFixed
            && cell.Day == 0
            && cell.Period == 1
            && cell.Grade == 1);

        var placed = viewModel.HandleStagedBlockDrop(0, 2, 1, 0);

        Assert.True(placed, viewModel.StatusMessage);
        Assert.Contains(viewModel.Grid.Cells, cell =>
            cell.Assignment.CourseId == "FIX-01"
            && cell.Assignment.IsSchoolFixed
            && cell.Day == 0
            && cell.Period == 2
            && cell.Grade == 1);
    }

    [Fact]
    public void MultiHourProfessorUnavailable_ShowsOneBlockViolation()
    {
        var course = Course("A-01", 1, "P1");
        course.HoursPerWeek = 3;
        course.BlockStructure = new List<int> { 3 };
        var dialog = new CapturingConflictDialogService();
        var viewModel = Create(
            new[] { course },
            new[]
            {
                new SolutionAssignment("A-01", 0, 1, "R1", "A"),
                new SolutionAssignment("A-01", 0, 2, "R1", "A"),
                new SolutionAssignment("A-01", 0, 3, "R1", "A"),
            },
            professors: new[]
            {
                new Professor
                {
                    Id = "P1",
                    Name = "P1",
                    UnavailableSlots = new List<TimeSlot>
                    {
                        new(0, 1),
                        new(0, 2),
                        new(0, 3),
                    },
                },
            },
            dialog: dialog);

        var panelItem = Assert.Single(viewModel.ConflictGroups.Where(item => item.Type == ConflictType.ProfUnavailable));
        var panelConflict = Assert.Single(panelItem.DetailConflicts);

        Assert.Equal(3, panelConflict.Assignments?.Count);
        Assert.Contains("1~3", panelConflict.Description);

        viewModel.ValidateAllCommand.Execute(null);

        var checks = Assert.Single(dialog.ValidationCheckCalls);
        var professorUnavailableCheck = checks.Single(check =>
            check.DetailConflicts.Any(conflict => conflict.Type == ConflictType.ProfUnavailable));
        var validationConflict = Assert.Single(professorUnavailableCheck.DetailConflicts);

        Assert.Equal(3, validationConflict.Assignments?.Count);
        Assert.Contains("1~3", validationConflict.Description);
    }

    private static ManualEditViewModel Create(
        IReadOnlyList<Course> courses,
        IReadOnlyList<SolutionAssignment> assignments,
        SchedulePolicy? policy = null,
        IReadOnlyList<Professor>? professors = null,
        IConflictDialogService? dialog = null)
    {
        policy ??= SchedulePolicy.Default;
        var snapshot = new AppData(
            courses.ToList(),
            professors?.ToList() ?? new List<Professor>
            {
                new() { Id = "P1", Name = "P1" },
                new() { Id = "P2", Name = "P2" },
                new() { Id = "P3", Name = "P3" },
            },
            new List<Room>
            {
                new() { Id = "R1", Name = "R1" },
                new() { Id = "R2", Name = "R2" },
                new() { Id = "R3", Name = "R3" },
            },
            new List<CrossGroup>(),
            new List<RetakeScenario>())
        {
            SchedulePolicy = policy,
        };
        var lunchPeriods = SchedulePolicyRules.StaticLunchPeriodsByDay(policy, Constants.Days);
        var viewModel = new ManualEditViewModel(
            WorkspaceService.CreateSession(snapshot),
            dialog ?? new NullConflictDialogService());
        viewModel.LoadFromSnapshot(
            snapshot,
            new RankedSolution(
                assignments,
                new SolutionScore(0, 0, 0, 0),
                lunchPeriods),
            "manual freedom");
        return viewModel;
    }

    private static UnifiedCell Cell(ManualEditViewModel viewModel, string courseId) =>
        viewModel.Grid.Cells.Single(cell => cell.Assignment.CourseId == courseId);

    private static UnifiedCell CellAt(ManualEditViewModel viewModel, string courseId, int day, int period) =>
        viewModel.Grid.Cells.Single(cell =>
            cell.Assignment.CourseId == courseId
            && cell.Day == day
            && cell.Period == period);

    private static void SetWorkingAssignments(ManualEditViewModel viewModel, params SolutionAssignment[] assignments)
    {
        var field = typeof(ManualEditViewModel).GetField(
            "_working",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(viewModel, assignments.ToList());
    }

    private static Course Course(string id, int grade, string professorId) =>
        new()
        {
            Id = id,
            Name = id,
            Grade = grade,
            HoursPerWeek = 1,
            ProfessorId = professorId,
            BlockStructure = new List<int> { 1 },
        };

    private sealed class CapturingConflictDialogService : IConflictDialogService
    {
        public List<IReadOnlyList<ValidationCheckItem>> ValidationCheckCalls { get; } = new();

        public bool ConfirmDespiteConflicts(IReadOnlyList<ConflictItem> newConflicts) => true;

        public void ShowValidationResult(string title, string message, IReadOnlyList<ConflictItem> conflicts)
        {
            ValidationCheckCalls.Add(Array.Empty<ValidationCheckItem>());
        }

        public void ShowValidationResult(
            string title,
            string message,
            IReadOnlyList<ConflictItem> conflicts,
            IReadOnlyList<ValidationCheckItem> checks)
        {
            ValidationCheckCalls.Add(checks);
        }
    }
}
