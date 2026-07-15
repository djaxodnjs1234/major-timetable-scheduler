using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Grid;

namespace TimetableScheduler.ViewModel.Pages;

public sealed record NamedGridViewModel(string Id, string Name, TimetableGridViewModel Grid);

public sealed partial class ResultsViewModel : PageViewModelBase
{
    private static readonly string[] DayNames = { "월", "화", "수", "목", "금" };

    private readonly WorkspaceService _workspace;

    // Snapshot of the workspace that produced the current solutions (session edit mode).
    // Null → use the live global workspace.
    private AppData? _sessionData;
    private IReadOnlyList<Course> SessionCourses =>
        (IReadOnlyList<Course>?)_sessionData?.Courses ?? _workspace.ExpandedCourses;
    private IReadOnlyList<Professor> SessionProfessors =>
        (IReadOnlyList<Professor>?)_sessionData?.Professors ?? _workspace.Professors;
    private IReadOnlyList<Room> SessionRooms =>
        (IReadOnlyList<Room>?)_sessionData?.Rooms ?? _workspace.Rooms;

    /// <summary>The snapshot behind the current solutions, for handing to manual editing.</summary>
    public AppData? SessionSnapshot => _sessionData;

    public AppData CurrentSnapshot => _sessionData ?? _workspace.SchedulingSnapshot();

    public override string Title => "해 미리보기";

    public ObservableCollection<RankedSolution> Solutions { get; } = new();
    public ObservableCollection<SolutionCardViewModel> SolutionCards { get; } = new();

    public UnifiedTimetableViewModel Unified { get; } = new();
    public ObservableCollection<NamedGridViewModel> GradeViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> RoomViews { get; } = new();
    public ObservableCollection<NamedGridViewModel> ProfessorViews { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedCommand))]
    private RankedSolution? selectedSolution;

    public event EventHandler? EditSelectedRequested;
    public event EventHandler? BackRequested;

    public ResultsViewModel(WorkspaceService workspace)
    {
        _workspace = workspace;
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void EditSelected() => EditSelectedRequested?.Invoke(this, EventArgs.Empty);

    private bool CanEditSelected() => SelectedSolution != null;

    [RelayCommand]
    private void KeepSolution(SolutionCardViewModel? card)
    {
        if (card is null)
            return;

        var name = $"Solution {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        _workspace.SaveTimetable(
            name,
            card.Solution.Assignment,
            snapshot: CurrentSnapshot,
            lunchPeriodsByDay: card.Solution.LunchPeriodsByDay);
        card.IsKept = true;
    }

    public void SetSolutions(IEnumerable<RankedSolution> ranked, AppData? sessionData = null)
    {
        _sessionData = sessionData;
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

        // Theoretical max is the sum of configured soft-constraint score weights.
        var maxScore = SolutionScoring.ScWeights.Values.Sum();

        // Global max course count across all solutions and days for consistent density scaling
        int globalMax = Solutions
            .SelectMany(s => Enumerable.Range(0, 5)
                .Select(d => s.Assignment.Where(a => a.Day == d)
                    .Select(a => a.CourseId).Distinct().Count()))
            .DefaultIfEmpty(1).Max();
        if (globalMax == 0) globalMax = 1;

        var schedulePolicy = CurrentSnapshot.SchedulePolicy;
        var globalMaxSlotCount = Solutions
            .SelectMany(s => s.Assignment
                .Where(a => !SchedulePolicyRules.IsLunch(
                    schedulePolicy,
                    s.LunchPeriodsByDay,
                    a.Day,
                    a.Period))
                .GroupBy(a => (a.Day, a.Period))
                .Select(g => g.Select(a => a.CourseId).Distinct().Count()))
            .DefaultIfEmpty(1)
            .Max();
        if (globalMaxSlotCount == 0) globalMaxSlotCount = 1;

        int rank = 1;
        foreach (var s in Solutions)
        {
            var days = Enumerable.Range(0, 5).Select(d => new DayDensityItem(
                DayNames[d],
                (double)s.Assignment.Where(a => a.Day == d)
                    .Select(a => a.CourseId).Distinct().Count() / globalMax
            )).ToList();

            var previewRows = BuildPreviewRows(
                s.Assignment,
                globalMaxSlotCount,
                schedulePolicy,
                s.LunchPeriodsByDay);

            var normalized = Math.Round(s.Score.Total / Math.Max(1.0, maxScore) * 100, 1);
            SolutionCards.Add(new SolutionCardViewModel(s, rank++, normalized, days, previewRows));
        }
    }

    private static IReadOnlyList<MiniPreviewRow> BuildPreviewRows(
        IReadOnlyList<SolutionAssignment> assignments,
        int maxSlotCount,
        SchedulePolicy schedulePolicy,
        IReadOnlyDictionary<int, int>? lunchPeriodsByDay)
    {
        var rows = new List<MiniPreviewRow>();
        for (var period = 1; period <= 9; period++)
        {
            var cells = new List<MiniPreviewCell>();
            for (var day = 0; day < 5; day++)
            {
                if (SchedulePolicyRules.IsLunch(
                        schedulePolicy,
                        lunchPeriodsByDay,
                        day,
                        period))
                {
                    cells.Add(new MiniPreviewCell(false, true, 0, 0));
                    continue;
                }

                var courseCount = assignments
                    .Where(a => a.Day == day && a.Period == period)
                    .Select(a => a.CourseId)
                    .Distinct()
                    .Count();

                if (courseCount == 0)
                {
                    cells.Add(new MiniPreviewCell(false, false, 0, 0));
                    continue;
                }

                cells.Add(new MiniPreviewCell(true, false, courseCount, (double)courseCount / maxSlotCount));
            }

            rows.Add(new MiniPreviewRow(period, false, cells));
        }

        return rows;
    }

    private void RenderCurrent()
    {
        var assignment = SelectedSolution?.Assignment ?? Array.Empty<Solver.SolutionAssignment>();
        var courses = SessionCourses;
        var professors = SessionProfessors;
        var rooms = SessionRooms;
        var schedulePolicy = CurrentSnapshot.SchedulePolicy;
        var lunchPeriodsByDay = SelectedSolution?.LunchPeriodsByDay;

        Unified.Render(
            assignment,
            courses,
            professors,
            rooms,
            schedulePolicy,
            lunchPeriodsByDay);

        GradeViews.Clear();
        foreach (var g in AcademicLevels.AllGrades)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(
                assignment,
                courses,
                (c, _) => c.Grade == g,
                professors,
                rooms,
                schedulePolicy,
                lunchPeriodsByDay);
            GradeViews.Add(new NamedGridViewModel(g.ToString(), AcademicLevels.DisplayName(g), vm));
        }

        RoomViews.Clear();
        foreach (var r in rooms)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(
                assignment,
                courses,
                (_, rid) => rid == r.Id,
                professors,
                rooms,
                schedulePolicy,
                lunchPeriodsByDay);
            RoomViews.Add(new NamedGridViewModel(r.Id, r.Name, vm));
        }

        ProfessorViews.Clear();
        foreach (var p in professors)
        {
            var vm = new TimetableGridViewModel();
            vm.Render(
                assignment,
                courses,
                (c, _) => IsCourseTaughtBy(c, p.Id),
                professors,
                rooms,
                schedulePolicy,
                lunchPeriodsByDay);
            ProfessorViews.Add(new NamedGridViewModel(p.Id, p.Name, vm));
        }
    }

    private static bool IsCourseTaughtBy(Course course, string professorId) =>
        string.Equals(course.ProfessorId, professorId, StringComparison.Ordinal)
        || course.CoteachProfs.Contains(professorId);
}
