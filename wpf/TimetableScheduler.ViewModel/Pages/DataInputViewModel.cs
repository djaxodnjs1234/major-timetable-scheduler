using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Pages;

public enum InputCategory { Professor, Course, Room, Solve }

/// <summary>
/// One item in the course list: either a group of non-fixed sections (shown as a
/// single row) or a single fixed section (shown individually).
/// </summary>
public sealed class CourseGroupItem
{
    public string BaseId { get; init; } = "";
    public string DisplayLabel { get; init; } = "";
    /// <summary>All sections in this group (N>1 for grouped, 1 for individual fixed).</summary>
    public List<Course> Sections { get; init; } = new();
    public bool IsFixedIndividual => Sections.Count == 1 && Sections[0].IsFixed;
}

public sealed partial class DataInputViewModel : PageViewModelBase
{
    private WorkspaceService _workspace;
    private readonly WorkspaceService _globalWorkspace;
    private readonly SolverService _solver;
    private CancellationTokenSource? _cts;
    private EventHandler? _workspaceChangedHandler;

    public override string Title => "정보 입력";

    public WorkspaceService Workspace => _workspace;

    /// <summary>True when editing an existing timetable's snapshot (session workspace).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoToManualCommand))]
    private bool isExistingMode;

    public ObservableCollection<CourseGroupItem> CourseGroups { get; } = new();

    [ObservableProperty]
    private InputCategory selectedCategory = InputCategory.Course;

    [ObservableProperty]
    private object? selectedItem;

    [ObservableProperty]
    private string newId = "";

    [ObservableProperty]
    private string newName = "";

    public bool IsProfessorSelected => SelectedCategory == InputCategory.Professor;
    public bool IsCourseSelected => SelectedCategory == InputCategory.Course;
    public bool IsRoomSelected => SelectedCategory == InputCategory.Room;
    public bool IsSolveSelected => SelectedCategory == InputCategory.Solve;

    partial void OnSelectedCategoryChanged(InputCategory value)
    {
        OnPropertyChanged(nameof(IsProfessorSelected));
        OnPropertyChanged(nameof(IsCourseSelected));
        OnPropertyChanged(nameof(IsRoomSelected));
        OnPropertyChanged(nameof(IsSolveSelected));
        SelectedItem = null;
    }

    [RelayCommand]
    private void SelectProfessor() => SelectedCategory = InputCategory.Professor;

    [RelayCommand]
    private void SelectCourse() => SelectedCategory = InputCategory.Course;

    [RelayCommand]
    private void SelectRoom() => SelectedCategory = InputCategory.Room;

    [RelayCommand]
    private void SelectSolve() => SelectedCategory = InputCategory.Solve;

    [RelayCommand]
    private void AddNew()
    {
        if (string.IsNullOrWhiteSpace(NewId)) return;
        switch (SelectedCategory)
        {
            case InputCategory.Professor:
                if (_workspace.Professors.Any(p => p.Id == NewId)) return;
                _workspace.AddProfessor(new Professor { Id = NewId, Name = NewName });
                break;
            case InputCategory.Course:
                if (_workspace.Courses.Any(c => c.Id == NewId)) return;
                _workspace.AddCourse(new Course
                {
                    Id = NewId, Name = NewName,
                    Grade = 1, HoursPerWeek = 3,
                    BlockStructure = new List<int> { 2, 1 },
                    CourseType = "전선",
                });
                break;
            case InputCategory.Room:
                if (_workspace.Rooms.Any(r => r.Id == NewId)) return;
                _workspace.AddRoom(new Room { Id = NewId, Name = NewName });
                break;
        }
        NewId = "";
        NewName = "";
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        switch (SelectedItem)
        {
            case Professor p: _workspace.DeleteProfessor(p.Id); break;
            case Course c: _workspace.DeleteCourse(c); break;
            case Room r: _workspace.DeleteRoom(r.Id); break;
        }
        SelectedItem = null;
    }

    [RelayCommand]
    private void SaveSelected()
    {
        switch (SelectedItem)
        {
            case Professor p: _workspace.UpdateProfessor(p); break;
            case Course c: _workspace.UpdateCourse(c); break;
            case Room r: _workspace.UpdateRoom(r); break;
        }
    }

    [RelayCommand]
    private void ImportXlsx(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        _workspace.ImportFromXlsx(path);
    }

    // ------------------------------------------------------------------
    // Course group commands (used by DataInputView course list)
    // ------------------------------------------------------------------

    /// <summary>Apply shared fields to every section in the group.</summary>
    [RelayCommand]
    private void SaveGroup(CourseGroupItem item)
    {
        if (item is null || item.Sections.Count == 0) return;
        var rep = item.Sections[0];
        foreach (var sec in item.Sections)
        {
            if (!ReferenceEquals(sec, rep))
            {
                sec.Name = rep.Name;
                sec.Grade = rep.Grade;
                sec.HoursPerWeek = rep.HoursPerWeek;
                sec.CourseType = rep.CourseType;
                sec.ProfessorId = rep.ProfessorId;
                sec.Department = rep.Department;
                sec.IsFixed = rep.IsFixed;
                sec.FixedRooms = new List<string>(rep.FixedRooms);
                sec.BlockStructure = new List<int>(rep.BlockStructure);
                sec.CoteachProfs = new List<string>(rep.CoteachProfs);
                // FixedSlots intentionally not copied — each section has its own slots
            }
            _workspace.UpdateCourse(sec);
        }
    }

    /// <summary>Delete all sections in the group.</summary>
    [RelayCommand]
    private void DeleteGroup(CourseGroupItem item)
    {
        if (item is null) return;
        foreach (var sec in item.Sections.ToList())
            _workspace.DeleteCourse(sec);
    }

    /// <summary>Save a single fixed section (IsFixedIndividual=true).</summary>
    [RelayCommand]
    private void SaveSection(CourseGroupItem item)
    {
        if (item is null || item.Sections.Count != 1) return;
        _workspace.UpdateCourse(item.Sections[0]);
    }

    /// <summary>Delete a single fixed section (IsFixedIndividual=true).</summary>
    [RelayCommand]
    private void DeleteSection(CourseGroupItem item)
    {
        if (item is null || item.Sections.Count != 1) return;
        _workspace.DeleteCourse(item.Sections[0]);
    }

    /// <summary>Add the next-numbered section to the group, copying shared fields from Sections[0].</summary>
    [RelayCommand]
    private void AddSection(CourseGroupItem item)
    {
        if (item is null || item.Sections.Count == 0) return;
        var rep = item.Sections[0];
        var next = item.Sections.Max(s => s.Section) + 1;
        var newId = $"{item.BaseId}-{next:D2}";
        if (_workspace.Courses.Any(c => c.Id == newId)) return;
        _workspace.AddCourse(new Course
        {
            Id = newId,
            Name = rep.Name,
            Grade = rep.Grade,
            HoursPerWeek = rep.HoursPerWeek,
            CourseType = rep.CourseType,
            ProfessorId = rep.ProfessorId,
            Department = rep.Department,
            Section = next,
            BlockStructure = new List<int>(rep.BlockStructure),
        });
    }

    /// <summary>Remove the last section from the group. No-op if only one section remains.</summary>
    [RelayCommand]
    private void RemoveSection(CourseGroupItem item)
    {
        if (item is null || item.Sections.Count <= 1) return;
        var last = item.Sections.OrderByDescending(s => s.Section).First();
        _workspace.DeleteCourse(last);
    }

    [ObservableProperty]
    private int totalSolutions = 3;

    [ObservableProperty]
    private int timeLimitSec = 120;

    [ObservableProperty]
    private bool useSc01 = true;

    [ObservableProperty]
    private bool useSc02 = true;

    [ObservableProperty]
    private bool useSc03 = true;

    [ObservableProperty]
    private string statusMessage = "";

    [ObservableProperty]
    private int progressCurrent;

    [ObservableProperty]
    private int progressTotal;

    [ObservableProperty]
    private int uniqueSolutionsFound;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SolveCommand))]
    private bool isSolving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoToSelectionCommand))]
    private bool isSolveComplete;

    public ObservableCollection<RankedSolution> RankedResults { get; } = new();

    public event EventHandler<IReadOnlyList<RankedSolution>>? SolveCompleted;
    public event EventHandler? GoToSelectionRequested;
    public event EventHandler? GoToManualRequested;

    [RelayCommand(CanExecute = nameof(CanGoToSelection))]
    private void GoToSelection() => GoToSelectionRequested?.Invoke(this, EventArgs.Empty);
    private bool CanGoToSelection() => IsSolveComplete;

    /// <summary>
    /// Skip the solver and hand the existing timetable's base assignments straight to
    /// manual editing. Only valid in existing-timetable mode (a base layout exists).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoToManual))]
    private void GoToManual() => GoToManualRequested?.Invoke(this, EventArgs.Empty);
    private bool CanGoToManual() => IsExistingMode;

    /// <summary>The base assignments + snapshot for handing off to manual editing.</summary>
    public RankedSolution? BuildEditHandoff()
    {
        if (_editBaseAssignments == null) return null;
        return new RankedSolution(_editBaseAssignments, new SolutionScore(0, 0, 0, 0));
    }

    public AppData CurrentSnapshot() => _workspace.Snapshot();

    public DataInputViewModel(WorkspaceService workspace, SolverService solver)
    {
        _globalWorkspace = workspace;
        _solver = solver;
        _workspaceChangedHandler = (_, _) =>
        {
            SolveCommand.NotifyCanExecuteChanged();
            RebuildCourseGroups();
        };
        _workspace = workspace;
        _workspace.Changed += _workspaceChangedHandler;
        RebuildCourseGroups();
    }

    private void SwitchWorkspace(WorkspaceService target)
    {
        if (ReferenceEquals(_workspace, target)) return;
        _workspace.Changed -= _workspaceChangedHandler;
        _workspace = target;
        _workspace.Changed += _workspaceChangedHandler;
        OnPropertyChanged(nameof(Workspace));
        RebuildCourseGroups();
        SolveCommand.NotifyCanExecuteChanged();
        SelectedItem = null;
        IsSolveComplete = false;
        StatusMessage = "";
    }

    /// <summary>Enter "new timetable" mode — edits the global workspace.</summary>
    public void LoadForNewTimetable()
    {
        SwitchWorkspace(_globalWorkspace);
        IsExistingMode = false;
        SelectedCategory = InputCategory.Course;
    }

    /// <summary>
    /// Enter "existing timetable" mode — edits an in-memory session workspace seeded
    /// from the timetable's constraint snapshot. The global workspace is untouched.
    /// </summary>
    public void LoadForExistingTimetable(SavedTimetableRecord record)
    {
        var snapshot = record.SnapshotJson is { Length: > 0 } json
            ? System.Text.Json.JsonSerializer.Deserialize<AppData>(json) ?? AppData.Empty()
            : AppData.Empty();
        SwitchWorkspace(WorkspaceService.CreateSession(snapshot));
        IsExistingMode = true;
        SelectedCategory = InputCategory.Course;
        _editBaseAssignments = record.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId))
            .ToList();
        _editBaseName = record.Name;
    }

    private IReadOnlyList<SolutionAssignment>? _editBaseAssignments;
    private string _editBaseName = "";

    /// <summary>Name of the timetable being re-edited (for the manual-edit save field).</summary>
    public string EditBaseName => _editBaseName;

    private static readonly string[] SectionLetters = { "A","B","C","D","E","F" };

    private static string SectionLetter(int section) =>
        section >= 1 && section <= SectionLetters.Length
            ? SectionLetters[section - 1] : section.ToString();

    private void RebuildCourseGroups()
    {
        CourseGroups.Clear();

        var byBase = _workspace.Courses
            .GroupBy(c => DomainHelpers.BaseId(c.Id))
            .OrderBy(g => g.First().Grade)
            .ThenBy(g => g.First().CourseType)
            .ThenBy(g => g.Key);

        foreach (var g in byBase)
        {
            var sections = g.OrderBy(c => c.Section).ToList();

            if (sections.Any(c => c.IsFixed))
            {
                // Fixed: one row per section
                foreach (var sec in sections)
                {
                    var letter = SectionLetter(sec.Section);
                    var label = sections.Count > 1
                        ? $"{sec.Id}  {sec.Name}  {sec.Grade}학년  ★"
                        : $"{sec.Id}  {sec.Name}  {sec.Grade}학년  ★";
                    CourseGroups.Add(new CourseGroupItem
                    {
                        BaseId = g.Key,
                        DisplayLabel = label,
                        Sections = new List<Course> { sec },
                    });
                }
            }
            else
            {
                // Non-fixed: one row for the whole group
                var rep = sections[0];
                var secPart = sections.Count > 1
                    ? $"  ({string.Join("·", sections.Select(s => SectionLetter(s.Section)))}분반)"
                    : "";
                var label = $"{g.Key}  {rep.Name}  {rep.Grade}학년  {rep.HoursPerWeek}h{secPart}";
                CourseGroups.Add(new CourseGroupItem
                {
                    BaseId = g.Key,
                    DisplayLabel = label,
                    Sections = sections,
                });
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanSolve))]
    private async Task SolveAsync()
    {
        IsSolving = true;
        IsSolveComplete = false;
        StatusMessage = "솔버 시작";
        ProgressCurrent = 0;
        ProgressTotal = TotalSolutions;
        UniqueSolutionsFound = 0;

        _cts = new CancellationTokenSource();
        var progress = new Progress<SolverProgress>(p =>
        {
            StatusMessage = $"[{p.Phase}] {p.Message}";
            ProgressCurrent = p.CurrentAttempt;
            ProgressTotal = p.TotalAttempts;
            UniqueSolutionsFound = p.UniqueSolutions;
        });

        try
        {
            var options = new DiverseSolverOptions
            {
                TotalSolutions = TotalSolutions,
                TimeLimitSec = TimeLimitSec,
                UseSc01 = UseSc01,
                UseSc02 = UseSc02,
                UseSc03 = UseSc03,
            };
            var result = await _solver.SolveAsync(_workspace, options, progress, _cts.Token);
            var ranked = _solver.Rank(_workspace, result, topM: 10);
            RankedResults.Clear();
            foreach (var r in ranked) RankedResults.Add(r);
            StatusMessage = $"완료: {result.Status}, {result.Solutions.Count}개 해, top {ranked.Count}";
            IsSolveComplete = true;
            SolveCompleted?.Invoke(this, ranked);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "취소됨";
        }
        catch (Exception ex)
        {
            StatusMessage = $"오류: {ex.Message}";
        }
        finally
        {
            IsSolving = false;
        }
    }

    private bool CanSolve() => !IsSolving && _workspace.Courses.Count > 0;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
