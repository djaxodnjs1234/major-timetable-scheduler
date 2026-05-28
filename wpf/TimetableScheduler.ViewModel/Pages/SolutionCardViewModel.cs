using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Scoring;

namespace TimetableScheduler.ViewModel.Pages;

public sealed record DayDensityItem(string DayName, double Density);

public sealed partial class SolutionCardViewModel : ObservableObject
{
    public RankedSolution Solution { get; }
    public int Rank { get; }
    public double NormalizedScore { get; }
    public IReadOnlyList<DayDensityItem> Days { get; }

    [ObservableProperty]
    private bool isSelected;

    public SolutionCardViewModel(
        RankedSolution solution, int rank, double normalizedScore,
        IReadOnlyList<DayDensityItem> days)
    {
        Solution = solution;
        Rank = rank;
        NormalizedScore = normalizedScore;
        Days = days;
    }
}
