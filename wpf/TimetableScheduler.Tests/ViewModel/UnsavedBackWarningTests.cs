using Microsoft.Extensions.DependencyInjection;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Tests.ViewModel;

public sealed class CodexUnsavedBackWarningTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"unsaved_back_{Guid.NewGuid():N}.db");
    private readonly WorkspaceService _workspace;

    public CodexUnsavedBackWarningTests()
    {
        _workspace = new WorkspaceService(new SqliteRepository(_dbPath));
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
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void Back_WithNoUnsavedEdits_RaisesBackRequestedImmediately()
    {
        var vm = new DataInputViewModel(_workspace, new SolverService());
        var navigations = 0;
        var confirmations = 0;
        vm.BackRequested += (_, _) => navigations++;
        vm.BackConfirmationRequested += (_, _) => confirmations++;

        vm.BackCommand.Execute(null);

        Assert.Equal(1, navigations);
        Assert.Equal(0, confirmations);
    }

    [Fact]
    public void Back_WithUnsavedEdits_RequestsConfirmationAndCancelsByDefault()
    {
        var vm = new DataInputViewModel(_workspace, new SolverService());
        vm.CourseGroups.Single().IsEditing = true;
        var navigations = 0;
        UnsavedBackNavigationEventArgs? confirmation = null;
        vm.BackRequested += (_, _) => navigations++;
        vm.BackConfirmationRequested += (_, args) => confirmation = args;

        vm.BackCommand.Execute(null);

        Assert.Equal(0, navigations);
        Assert.NotNull(confirmation);
        Assert.Contains("Course", confirmation!.UnsavedEditLabels.Single());
    }

    [Fact]
    public void Back_WithUnsavedEdits_NavigatesWhenConfirmationAllows()
    {
        var vm = new DataInputViewModel(_workspace, new SolverService());
        vm.ProfessorItems.Single().IsEditing = true;
        var navigations = 0;
        vm.BackRequested += (_, _) => navigations++;
        vm.BackConfirmationRequested += (_, args) => args.ShouldContinue = true;

        vm.BackCommand.Execute(null);

        Assert.Equal(1, navigations);
    }

    [Fact]
    public void Back_WithCommittedSessionDataChange_RequestsConfirmation()
    {
        var vm = new DataInputViewModel(_workspace, new SolverService());
        vm.LoadForNewTimetable();
        vm.Workspace.AddRoom(new Room { Id = "R2", Name = "New Room" });
        var navigations = 0;
        UnsavedBackNavigationEventArgs? confirmation = null;
        vm.BackRequested += (_, _) => navigations++;
        vm.BackConfirmationRequested += (_, args) => confirmation = args;

        vm.BackCommand.Execute(null);

        Assert.Equal(0, navigations);
        Assert.NotNull(confirmation);
        Assert.Contains(confirmation!.UnsavedEditLabels, label => label.Contains("DB", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Back_WithGeneratedSolutions_RequestsConfirmationAndStays()
    {
        var vm = new DataInputViewModel(_workspace, new GeneratedSolutionSolverService())
        {
            TotalSolutions = 1,
        };
        await vm.SolveCommand.ExecuteAsync(null);
        Assert.True(vm.IsSolveComplete);
        Assert.Single(vm.RankedResults);
        var navigations = 0;
        UnsavedBackNavigationEventArgs? confirmation = null;
        vm.BackRequested += (_, _) => navigations++;
        vm.BackConfirmationRequested += (_, args) => confirmation = args;

        vm.BackCommand.Execute(null);

        Assert.Equal(0, navigations);
        Assert.NotNull(confirmation);
        Assert.Contains(
            confirmation!.UnsavedEditLabels,
            label => label.Contains("\uC0DD\uC131 \uC2DC\uAC04\uD45C", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Back_AfterLoadingNewTimetable_ClearsGeneratedSolutionWarning()
    {
        var vm = new DataInputViewModel(_workspace, new GeneratedSolutionSolverService())
        {
            TotalSolutions = 1,
        };
        await vm.SolveCommand.ExecuteAsync(null);
        vm.LoadForNewTimetable();
        var navigations = 0;
        var confirmations = 0;
        vm.BackRequested += (_, _) => navigations++;
        vm.BackConfirmationRequested += (_, _) => confirmations++;

        vm.BackCommand.Execute(null);

        Assert.Equal(1, navigations);
        Assert.Equal(0, confirmations);
    }

    [Fact]
    public void ManualBack_WithChangedWorkingTimetable_RequestsConfirmationAndStays()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"manual_back_{Guid.NewGuid():N}.db");
        ServiceProvider? services = null;
        try
        {
            var collection = new ServiceCollection();
            collection.AddTimetableScheduler(dbPath);
            services = collection.BuildServiceProvider();
            var workspace = services.GetRequiredService<WorkspaceService>();
            workspace.AddRoom(new Room { Id = "R1", Name = "Room One" });
            workspace.AddRoom(new Room { Id = "R2", Name = "Room Two" });
            workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor" });
            workspace.AddCourse(new Course
            {
                Id = "C-01",
                Name = "Course",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                Section = 1,
                BlockStructure = new List<int> { 1 },
            });
            var record = workspace.SaveTimetable(
                "manual-back",
                new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
                snapshot: workspace.Snapshot());
            var main = services.GetRequiredService<MainWindowViewModel>();
            main.Selection.EditCommand.Execute(record);
            var cell = main.Manual.Grid.Cells.First(c => c.Assignment.CourseId == "C-01");
            main.Manual.SelectCell(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx, cell.Assignment);
            main.Manual.NewRoomId = "R2";
            main.Manual.ApplyRoomChangeCommand.Execute(null);
            UnsavedBackNavigationEventArgs? confirmation = null;
            main.BackConfirmationRequested += (_, args) => confirmation = args;

            main.Manual.BackCommand.Execute(null);

            Assert.NotNull(confirmation);
            Assert.Same(main.Manual, main.CurrentPage);
        }
        finally
        {
            services?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    public void ManualBack_WithChangedWorkingTimetable_NavigatesWhenConfirmationAllows()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"manual_back_yes_{Guid.NewGuid():N}.db");
        ServiceProvider? services = null;
        try
        {
            var collection = new ServiceCollection();
            collection.AddTimetableScheduler(dbPath);
            services = collection.BuildServiceProvider();
            var workspace = services.GetRequiredService<WorkspaceService>();
            workspace.AddRoom(new Room { Id = "R1", Name = "Room One" });
            workspace.AddRoom(new Room { Id = "R2", Name = "Room Two" });
            workspace.AddProfessor(new Professor { Id = "P1", Name = "Professor" });
            workspace.AddCourse(new Course
            {
                Id = "C-01",
                Name = "Course",
                Grade = 1,
                HoursPerWeek = 1,
                ProfessorId = "P1",
                Section = 1,
                BlockStructure = new List<int> { 1 },
            });
            var record = workspace.SaveTimetable(
                "manual-back",
                new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
                snapshot: workspace.Snapshot());
            var main = services.GetRequiredService<MainWindowViewModel>();
            main.Selection.EditCommand.Execute(record);
            var cell = main.Manual.Grid.Cells.First(c => c.Assignment.CourseId == "C-01");
            main.Manual.SelectCell(cell.Day, cell.Period, cell.Grade, cell.SubColumnIdx, cell.Assignment);
            main.Manual.NewRoomId = "R2";
            main.Manual.ApplyRoomChangeCommand.Execute(null);
            main.BackConfirmationRequested += (_, args) => args.ShouldContinue = true;

            main.Manual.BackCommand.Execute(null);

            Assert.Same(main.Selection, main.CurrentPage);
        }
        finally
        {
            services?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private sealed class GeneratedSolutionSolverService : SolverService
    {
        public override Task<DiverseSolverResult> SolveAsync(
            WorkspaceService workspace,
            DiverseSolverOptions options,
            IProgress<SolverProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiverseSolverResult(
                "OPTIMAL",
                new IReadOnlyList<SolutionAssignment>[]
                {
                    new[] { new SolutionAssignment("C-01", 0, 1, "R1") },
                },
                null,
                null,
                null,
                1));
        }
    }
}
