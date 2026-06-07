using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.Tests.ViewModel;

public class TimetableGridViewModelTests
{
    [Fact]
    public void Constructor_Builds5x9Cells()
    {
        var vm = new TimetableGridViewModel();
        Assert.Equal(5 * 9, vm.Cells.Count);
    }

    [Fact]
    public void CellAt_ReturnsCorrectCell()
    {
        var vm = new TimetableGridViewModel();
        var cell = vm.CellAt(2, 4);
        Assert.Equal(2, cell.Day);
        Assert.Equal(4, cell.Period);
    }

    [Fact]
    public void Render_PopulatesOccupiedCells()
    {
        var vm = new TimetableGridViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "테스트", Grade = 2, ProfessorId = "P1", Section = 1 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 1, 3, "R1"),
        };
        vm.Render(assignment, courses);

        var cell = vm.CellAt(1, 3);
        Assert.True(cell.IsOccupied);
        Assert.Single(cell.Items);
        Assert.Equal("X-01", cell.Items[0].CourseId);
        Assert.Equal(new[] { "R1" }, cell.Items[0].Rooms);
        Assert.Equal(1, cell.Items[0].RowSpan);

        var emptyCell = vm.CellAt(0, 1);
        Assert.False(emptyCell.IsOccupied);
        Assert.Empty(emptyCell.Items);
    }

    [Fact]
    public void Render_GradeFilter_HidesNonMatching()
    {
        var vm = new TimetableGridViewModel();
        var courses = new List<Course>
        {
            new() { Id = "G2", Grade = 2 },
            new() { Id = "G3", Grade = 3 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("G2", 0, 1, "R"),
            new("G3", 0, 2, "R"),
        };
        vm.Render(assignment, courses, (c, _) => c.Grade == 2);

        Assert.True(vm.CellAt(0, 1).IsOccupied);
        Assert.False(vm.CellAt(0, 2).IsOccupied);
    }

    [Fact]
    public void Render_RoomFilter_HidesNonMatching()
    {
        var vm = new TimetableGridViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X", Grade = 2 },
            new() { Id = "Y", Grade = 2 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X", 0, 1, "R1"),
            new("Y", 0, 2, "R2"),
        };
        vm.Render(assignment, courses, (_, rid) => rid == "R1");

        Assert.True(vm.CellAt(0, 1).IsOccupied);
        Assert.False(vm.CellAt(0, 2).IsOccupied);
    }

    [Fact]
    public void Render_ConsecutivePeriods_MergedIntoOneCellWithRowSpan()
    {
        var vm = new TimetableGridViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "테스트", Grade = 2 },
        };
        // Same course, same day, periods 1 and 2 (consecutive)
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R"),
            new("X-01", 0, 2, "R"),
        };
        vm.Render(assignment, courses);

        // Start cell at period 1 has RowSpan=2
        Assert.True(vm.CellAt(0, 1).IsOccupied);
        Assert.Equal(2, vm.CellAt(0, 1).Items[0].RowSpan);
        // Inside cell at period 2 is empty
        Assert.False(vm.CellAt(0, 2).IsOccupied);
    }

    [Fact]
    public void Render_MultipleRoomsForSameSlot_AggregatedIntoOneItem()
    {
        var vm = new TimetableGridViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "캡스톤", Grade = 4 },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
            new("X-01", 0, 1, "R2"),
        };
        vm.Render(assignment, courses);

        var cell = vm.CellAt(0, 1);
        Assert.Single(cell.Items);
        Assert.Equal(2, cell.Items[0].Rooms.Count);
    }

    [Fact]
    public void Render_UsesProfessorAndRoomNamesForDisplayLabels()
    {
        var vm = new TimetableGridViewModel();
        var courses = new List<Course>
        {
            new() { Id = "X-01", Name = "캡스톤", Grade = 4, ProfessorId = "P1" },
        };
        var professors = new List<Professor>
        {
            new() { Id = "P1", Name = "김교수" },
        };
        var rooms = new List<Room>
        {
            new() { Id = "R1", Name = "공학관 101" },
        };
        var assignment = new List<SolutionAssignment>
        {
            new("X-01", 0, 1, "R1"),
        };

        vm.Render(assignment, courses, professors: professors, rooms: rooms);

        var item = Assert.Single(vm.CellAt(0, 1).Items);
        Assert.Equal("김교수", item.ProfessorLabel);
        Assert.Equal("공학관 101", item.RoomsLabel);
        Assert.Equal("P1", item.ProfessorId);
        Assert.Equal(new[] { "R1" }, item.Rooms);
    }

    [Fact]
    public void Render_TwoSectionsSameSlot_BothShownInCell()
    {
        var vm = new TimetableGridViewModel();
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

        var cell = vm.CellAt(0, 1);
        Assert.Equal(2, cell.Items.Count);
    }
}
