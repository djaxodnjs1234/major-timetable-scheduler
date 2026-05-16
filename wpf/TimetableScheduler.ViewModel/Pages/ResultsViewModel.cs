using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.ViewModel.Pages;

public sealed record NamedGridViewModel(string Id, string Name, TimetableGridViewModel Grid);

public sealed partial class ResultsViewModel : PageViewModelBase
{
    private readonly WorkspaceService _workspace;

    public override string Title => "해 미리보기";

    public ObservableCollection<RankedSolution> Solutions { get; } = new();

    public UnifiedTimetableViewModel Unified { get; } = new();
    public ObservableCollection<NamedGridViewModel> GradeViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> RoomViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> ProfessorViews { get; } = new();

    [ObservableProperty]
    private RankedSolution? selectedSolution;

    public ResultsViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public void SetSolutions(IEnumerable<RankedSolution> ranked)
    {
        Solutions.Clear();
        foreach (var r in ranked) Solutions.Add(r);
        SelectedSolution = Solutions.FirstOrDefault();
    }

    partial void OnSelectedSolutionChanged(RankedSolution? value) => RenderCurrent();

    private void RenderCurrent()
    {
        var assignment = SelectedSolution?.Assignment ?? Array.Empty<Solver.SolutionAssignment>();
        var courses = _workspace.Courses;

        Unified.Render(assignment, courses);

        GradeViews.Clear();
        foreach (var g in new[] { 1, 2, 3, 4 })
        {
            var vm = new TimetableGridViewModel();
            vm.Render(assignment, courses, (c, _) => c.Grade == g);
            GradeViews.Add(new NamedGridViewModel(g.ToString(), $"{g}학년", vm));
        }

        RoomViews.Clear();
        foreach (var r in _workspace.Rooms)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(assignment, courses, (_, rid) => rid == r.Id);
            RoomViews.Add(new NamedGridViewModel(r.Id, r.Name, vm));
        }

        ProfessorViews.Clear();
        foreach (var p in _workspace.Professors)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(assignment, courses, (c, _) => c.ProfessorId == p.Id);
            ProfessorViews.Add(new NamedGridViewModel(p.Id, p.Name, vm));
        }
    }
}
