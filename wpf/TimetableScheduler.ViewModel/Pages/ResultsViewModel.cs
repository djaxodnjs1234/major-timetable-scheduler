using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.ViewModel.Pages;

public sealed record NamedGridViewModel(string Id, string Name, TimetableGridViewModel Grid);

public sealed partial class ResultsViewModel : PageViewModelBase
{
    private static readonly string[] DayNames = { "월", "화", "수", "목", "금" };

    private readonly WorkspaceService _workspace;

    public override string Title => "해 미리보기";

    public ObservableCollection<RankedSolution> Solutions { get; } = new();
    public ObservableCollection<SolutionCardViewModel> SolutionCards { get; } = new();

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

        BuildCards();
        SelectedSolution = Solutions.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectCard(SolutionCardViewModel card)
    {
        SelectedSolution = card.Solution;
    }

    partial void OnSelectedSolutionChanged(RankedSolution? value)
    {
        foreach (var c in SolutionCards)
            c.IsSelected = c.Solution == value;
        RenderCurrent();
    }

    private void BuildCards()
    {
        SolutionCards.Clear();
        if (Solutions.Count == 0) return;

        // Theoretical max = SC01+SC02+SC03 weights each 1.0 → 3.0
        const double maxScore = 3.0;

        // Global max course count across all solutions and days for consistent density scaling
        int globalMax = Solutions
            .SelectMany(s => Enumerable.Range(0, 5)
                .Select(d => s.Assignment.Where(a => a.Day == d)
                    .Select(a => a.CourseId).Distinct().Count()))
            .DefaultIfEmpty(1).Max();
        if (globalMax == 0) globalMax = 1;

        int rank = 1;
        foreach (var s in Solutions)
        {
            var days = Enumerable.Range(0, 5).Select(d => new DayDensityItem(
                DayNames[d],
                (double)s.Assignment.Where(a => a.Day == d)
                    .Select(a => a.CourseId).Distinct().Count() / globalMax
            )).ToList();

            var normalized = Math.Round(s.Score.Total / maxScore * 100, 1);
            SolutionCards.Add(new SolutionCardViewModel(s, rank++, normalized, days));
        }
    }

    private void RenderCurrent()
    {
        var assignment = SelectedSolution?.Assignment ?? Array.Empty<Solver.SolutionAssignment>();
        var courses = _workspace.ExpandedCourses;

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
