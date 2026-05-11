using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Scoring;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.ViewModel.Pages;

public sealed partial class TimetableSelectionViewModel : PageViewModelBase
{
    private readonly WorkspaceService _workspace;

    public override string Title => "시간표 선택";

    public ObservableCollection<RankedSolution> Solutions { get; } = new();

    public TimetableGridViewModel Preview { get; } = new();

    [ObservableProperty]
    private RankedSolution? selectedSolution;

    public TimetableSelectionViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
    }

    public void SetSolutions(IEnumerable<RankedSolution> ranked)
    {
        Solutions.Clear();
        foreach (var r in ranked) Solutions.Add(r);
        SelectedSolution = Solutions.FirstOrDefault();
    }

    partial void OnSelectedSolutionChanged(RankedSolution? value)
    {
        if (value == null)
        {
            Preview.Render(Array.Empty<Solver.SolutionAssignment>(), Array.Empty<Domain.Course>());
            return;
        }
        Preview.Render(value.Assignment, _workspace.Courses);
    }
}
