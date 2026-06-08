using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Scoring;

namespace TimetableScheduler.ViewModel.Pages;

public sealed record DayDensityItem(string DayName, double Density);

public sealed record MiniPreviewCell(bool IsOccupied, bool IsLunch, int CourseCount, double Density);

public sealed record MiniPreviewRow(int Period, bool IsLunch, IReadOnlyList<MiniPreviewCell> Cells);

public sealed partial class SolutionCardViewModel : ObservableObject
{
    public RankedSolution Solution { get; }
    public int Rank { get; }
    public double NormalizedScore { get; }
    public IReadOnlyList<DayDensityItem> Days { get; }
    public IReadOnlyList<MiniPreviewRow> PreviewRows { get; }

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isKept;

    public SolutionCardViewModel(
        RankedSolution solution, int rank, double normalizedScore,
        IReadOnlyList<DayDensityItem> days,
        IReadOnlyList<MiniPreviewRow> previewRows)
    {
        Solution = solution;
        Rank = rank;
        NormalizedScore = normalizedScore;
        Days = days;
        PreviewRows = previewRows;
    }
}
