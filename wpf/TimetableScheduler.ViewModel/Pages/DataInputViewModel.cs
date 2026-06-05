using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Scoring;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Editors;

namespace TimetableScheduler.ViewModel.Pages;

public enum InputCategory { Professor, Course, Room, Solve }

public sealed record ManualEditHandoff(
    RankedSolution Solution,
    IReadOnlyList<SavedManualCrossLinkRow> ManualCrossLinks);

public sealed record CrossGroupListItem(string Id, string Display);

public sealed record CrossCandidateGradeGroup(string Header, ObservableCollection<CheckListItem> Items);

public sealed partial class ProfessorItem : ObservableObject
{
    public Professor Professor { get; init; } = new();
    public string HeaderId => Professor.Id;
    public string HeaderName => Professor.Name;
    public string HeaderUnavailableSlots { get; init; } = "-";
    public string HeaderUnavailableRooms { get; init; } = "-";
    public bool IsImportedFromExcel { get; init; }

    [ObservableProperty]
    private bool isEditing;
}

public sealed partial class RoomItem : ObservableObject
{
    public Room Room { get; init; } = new();
    public string HeaderId => Room.Id;
    public string HeaderName => Room.Name;
    public string HeaderLab => Room.IsLab ? "실습실" : "일반";
    public string HeaderCapacity => Room.Capacity > 0 ? $"{Room.Capacity}명" : "-";
    public bool IsImportedFromExcel { get; init; }

    [ObservableProperty]
    private bool isEditing;
}

/// <summary>
/// One item in the course list: either a group of non-fixed sections (shown as a
/// single row) or a single fixed section (shown individually).
/// </summary>
public sealed partial class CourseGroupItem : ObservableObject
{
    public string BaseId { get; init; } = "";
    public string DisplayLabel { get; init; } = "";
    public string HeaderCode { get; init; } = "";
    public string HeaderName { get; init; } = "";
    public string HeaderGrade { get; init; } = "";
    public string HeaderHours { get; init; } = "";
    public string HeaderSectionInfo { get; init; } = "";
    public string HeaderProfessor { get; init; } = "";
    public string HeaderBlockStructure { get; init; } = "";
    public string HeaderUnavailableRooms { get; init; } = "";
    public string HeaderCoteachProfessors { get; init; } = "";
    public string HeaderFixedTimes { get; init; } = "";
    public string HeaderCrossGroups { get; init; } = "";
    public bool IsImportedFromExcel { get; init; }
    /// <summary>All sections in this group (N>1 for grouped, 1 for individual fixed).</summary>
    public List<Course> Sections { get; init; } = new();
    public bool IsFixedIndividual => Sections.Count == 1 && Sections[0].IsFixed;

    [ObservableProperty]
    private bool isEditing;
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

    public bool IsSessionMode => _workspace.IsSession;

    /// <summary>True when editing an existing timetable's snapshot (session workspace).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoToManualCommand))]
    private bool isExistingMode;

    public ObservableCollection<CourseGroupItem> CourseGroups { get; } = new();
    public ObservableCollection<ProfessorItem> ProfessorItems { get; } = new();
    public ObservableCollection<RoomItem> RoomItems { get; } = new();

    public ObservableCollection<CrossGroupListItem> CrossGroupItems { get; } = new();
    public ObservableCollection<CheckListItem> CrossCandidateItems { get; } = new();
    public ObservableCollection<CrossCandidateGradeGroup> CrossCandidateGradeGroups { get; } = new();

    public int[] GradeOptions { get; } = { 1, 2, 3, 4 };
    public int[] HourOptions { get; } = { 1, 2, 3, 4 };
    public string[] CourseTypeOptions { get; } = { "전필", "전선", "교양" };
    public string[] BlockStructureOptions { get; } = { "1", "2", "2,1", "1,1", "3", "2,2", "2,1,1" };

    [ObservableProperty]
    private InputCategory selectedCategory = InputCategory.Course;

    [ObservableProperty]
    private object? selectedItem;

    [ObservableProperty]
    private string newId = "";

    [ObservableProperty]
    private string newName = "";

    [ObservableProperty]
    private CrossGroupListItem? selectedCrossGroup;

    [ObservableProperty]
    private string crossStatusMessage = "";

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
        switch (SelectedCategory)
        {
            case InputCategory.Professor:
                if (string.IsNullOrWhiteSpace(NewName)) return;
                _workspace.AddProfessor(new Professor { Id = NextProfessorId(), Name = NewName.Trim() });
                break;
            case InputCategory.Course:
                if (string.IsNullOrWhiteSpace(NewName)) return;
                _workspace.AddCourse(new Course
                {
                    Id = NextCourseBaseId(), Name = NewName.Trim(),
                    Grade = 1, HoursPerWeek = 3,
                    BlockStructure = new List<int> { 2, 1 },
                    CourseType = "전선",
                    UnavailableRooms = new List<string>(),
                });
                break;
            case InputCategory.Room:
                if (string.IsNullOrWhiteSpace(NewName)) return;
                _workspace.AddRoom(new Room { Id = NextRoomId(), Name = NewName.Trim() });
                break;
        }
        NewId = "";
        NewName = "";
    }

    [RelayCommand]
    private void AddCross()
    {
        var chosen = CrossCandidateItems
            .Where(item => item.IsChecked)
            .Select(item => item.Id)
            .ToList();
        if (chosen.Count != 2)
        {
            CrossStatusMessage = "Cross는 과목 2개만 선택할 수 있습니다.";
            return;
        }

        var groups = CourseBaseGroups();
        var sectionCounts = chosen.Select(id => groups[id].Count).Distinct().ToList();
        if (sectionCounts.Count > 1)
        {
            CrossStatusMessage = "Cross로 묶으려면 각 과목의 분반 수가 같아야 합니다.";
            return;
        }

        var hours = chosen.Select(id => groups[id][0].HoursPerWeek).Distinct().ToList();
        if (hours.Count > 1)
        {
            CrossStatusMessage = "Cross로 묶으려면 총 시수가 동일해야 합니다.";
            return;
        }

        var alreadyUsed = _workspace.CrossGroups
            .SelectMany(g => g.BaseIds)
            .Where(chosen.Contains)
            .Distinct()
            .ToList();
        if (alreadyUsed.Count > 0)
        {
            CrossStatusMessage = $"이미 다른 Cross에 속한 과목이 있습니다: {string.Join(", ", alreadyUsed)}";
            return;
        }

        _workspace.AddCrossGroup(new CrossGroup
        {
            Id = NextCrossId(),
            BaseIds = chosen,
        });
        CrossStatusMessage = "Cross를 추가했습니다.";
        RebuildCrossManager(clearCandidateSelection: true);
        RebuildCourseGroups();
    }

    [RelayCommand]
    private void DeleteCross(CrossGroupListItem? item)
    {
        if (item is null) return;
        _workspace.DeleteCrossGroup(item.Id);
        CrossStatusMessage = "선택한 Cross를 삭제했습니다.";
        SelectedCrossGroup = null;
        RebuildCrossManager();
        RebuildCourseGroups();
    }

    private string NextCrossId()
    {
        var ids = _workspace.CrossGroups.Select(g => g.Id).ToHashSet();
        for (var n = 1; ; n++)
        {
            var id = $"X{n:000}";
            if (!ids.Contains(id)) return id;
        }
    }

    private string NextProfessorId() => NextNumericId(_workspace.Professors.Select(p => p.Id));

    private string NextRoomId() => NextNumericId(_workspace.Rooms.Select(r => r.Id));

    private static string NextNumericId(IEnumerable<string> ids)
    {
        var max = ids
            .Select(id => int.TryParse(id, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString();
    }

    private string NextCourseBaseId()
    {
        var max = _workspace.Courses
            .Select(c => DomainHelpers.BaseId(c.Id))
            .Select(id => int.TryParse(id, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return (max + 1).ToString();
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
    private void EditProfessor(ProfessorItem item)
    {
        if (item is null) return;
        item.IsEditing = true;
    }

    [RelayCommand]
    private void SaveProfessor(ProfessorItem item)
    {
        if (item is null) return;
        _workspace.UpdateProfessor(item.Professor);
        item.IsEditing = false;
    }

    [RelayCommand]
    private void DeleteProfessorItem(ProfessorItem item)
    {
        if (item is null) return;
        _workspace.DeleteProfessor(item.Professor.Id);
    }

    [RelayCommand]
    private void EditRoom(RoomItem item)
    {
        if (item is null) return;
        item.IsEditing = true;
    }

    [RelayCommand]
    private void SaveRoom(RoomItem item)
    {
        if (item is null) return;
        _workspace.UpdateRoom(item.Room);
        item.IsEditing = false;
    }

    [RelayCommand]
    private void DeleteRoomItem(RoomItem item)
    {
        if (item is null) return;
        _workspace.DeleteRoom(item.Room.Id);
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
                sec.UnavailableRooms = new List<string>(rep.UnavailableRooms);
                sec.BlockStructure = new List<int>(rep.BlockStructure);
                sec.CoteachProfs = new List<string>(rep.CoteachProfs);
                // FixedSlots intentionally not copied — each section has its own slots
            }
            _workspace.UpdateCourse(sec);
        }
        item.IsEditing = false;
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
        item.IsEditing = false;
    }

    [RelayCommand]
    private void EditCourse(CourseGroupItem item)
    {
        if (item is null) return;
        item.IsEditing = true;
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
            FixedRooms = new List<string>(rep.FixedRooms),
            UnavailableRooms = new List<string>(rep.UnavailableRooms),
            CoteachProfs = new List<string>(rep.CoteachProfs),
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
    private bool useAdvancedPerSolveTimeSec;

    [ObservableProperty]
    private int perSolveTimeSec = new DiverseSolverOptions().PerSolveTimeSec;

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
    public event EventHandler? BackRequested;

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

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

    /// <summary>The base assignments + saved manual cross links for handing off to manual editing.</summary>
    public ManualEditHandoff? BuildEditHandoff()
    {
        if (_editBaseAssignments == null) return null;
        var solution = new RankedSolution(_editBaseAssignments, new SolutionScore(0, 0, 0, 0));
        return new ManualEditHandoff(solution, _editBaseManualCrossLinks);
    }

    public AppData CurrentSnapshot() => _workspace.Snapshot();

    public DataInputViewModel(WorkspaceService workspace, SolverService solver)
    {
        _globalWorkspace = workspace;
        _solver = solver;
        _workspaceChangedHandler = (_, _) =>
        {
            SolveCommand.NotifyCanExecuteChanged();
            RebuildProfessorItems();
            RebuildCourseGroups();
            RebuildRoomItems();
            RebuildCrossManager();
        };
        _workspace = workspace;
        _workspace.Changed += _workspaceChangedHandler;
        RebuildProfessorItems();
        RebuildCourseGroups();
        RebuildRoomItems();
        RebuildCrossManager();
    }

    private void SwitchWorkspace(WorkspaceService target)
    {
        if (ReferenceEquals(_workspace, target)) return;
        _workspace.Changed -= _workspaceChangedHandler;
        _workspace = target;
        _workspace.Changed += _workspaceChangedHandler;
        OnPropertyChanged(nameof(Workspace));
        OnPropertyChanged(nameof(IsSessionMode));
        RebuildProfessorItems();
        RebuildCourseGroups();
        RebuildRoomItems();
        RebuildCrossManager();
        SolveCommand.NotifyCanExecuteChanged();
        SelectedItem = null;
        IsSolveComplete = false;
        StatusMessage = "";
    }

    public void LoadForNewTimetable()
    {
        SwitchWorkspace(WorkspaceService.CreateSession(AppData.Empty()));
        IsExistingMode = false;
        _editBaseAssignments = null;
        _editBaseManualCrossLinks = Array.Empty<SavedManualCrossLinkRow>();
        _editBaseName = "";
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
        _editBaseManualCrossLinks = (record.ManualCrossLinks ?? Array.Empty<SavedManualCrossLinkRow>())
            .ToList();
        _editBaseName = record.Name;
    }

    private IReadOnlyList<SolutionAssignment>? _editBaseAssignments;
    private IReadOnlyList<SavedManualCrossLinkRow> _editBaseManualCrossLinks = Array.Empty<SavedManualCrossLinkRow>();
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
        var profNames = _workspace.Professors.ToDictionary(p => p.Id, p => p.Name);
        var roomNames = _workspace.Rooms.ToDictionary(r => r.Id, r => r.Name);

        var byBase = _workspace.Courses
            .GroupBy(c => DomainHelpers.BaseId(c.Id))
            .OrderBy(g => SortNumericFirst(g.Key));

        foreach (var g in byBase)
        {
            var sections = g.OrderBy(c => c.Section).ToList();

            if (sections.Any(c => c.IsFixed))
            {
                // Fixed: one row per section
                foreach (var sec in sections)
                {
                    var label = $"{sec.Id}  {sec.Name}  {sec.Grade}학년  ★";
                    CourseGroups.Add(new CourseGroupItem
                    {
                        BaseId = g.Key,
                        DisplayLabel = label,
                        HeaderCode = sec.Id,
                        HeaderName = sec.Name,
                        HeaderGrade = $"{sec.Grade}학년",
                        HeaderHours = $"{sec.HoursPerWeek}시간",
                        HeaderSectionInfo = "1개",
                        HeaderProfessor = DisplayName(sec.ProfessorId, profNames),
                        HeaderBlockStructure = FormatBlocks(sec),
                        HeaderUnavailableRooms = FormatNames(sec.UnavailableRooms, roomNames),
                        HeaderCoteachProfessors = FormatNames(sec.CoteachProfs, profNames),
                        HeaderFixedTimes = FormatFixedTimes(new[] { sec }),
                        HeaderCrossGroups = FormatCrossGroups(g.Key),
                        IsImportedFromExcel = !string.IsNullOrWhiteSpace(sec.Department),
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
                    HeaderCode = g.Key,
                    HeaderName = rep.Name,
                    HeaderGrade = $"{rep.Grade}학년",
                    HeaderHours = $"{rep.HoursPerWeek}시간",
                    HeaderSectionInfo = $"{sections.Count}개",
                    HeaderProfessor = DisplayName(rep.ProfessorId, profNames),
                    HeaderBlockStructure = FormatBlocks(rep),
                    HeaderUnavailableRooms = FormatNames(rep.UnavailableRooms, roomNames),
                    HeaderCoteachProfessors = FormatNames(rep.CoteachProfs, profNames),
                    HeaderFixedTimes = "-",
                    HeaderCrossGroups = FormatCrossGroups(g.Key),
                    IsImportedFromExcel = sections.Any(s => !string.IsNullOrWhiteSpace(s.Department)),
                    Sections = sections,
                });
            }
        }
    }

    private void RebuildProfessorItems()
    {
        ProfessorItems.Clear();
        var roomNames = _workspace.Rooms.ToDictionary(r => r.Id, RoomDisplayLabel);
        var importedProfIds = _workspace.Courses
            .Where(c => !string.IsNullOrWhiteSpace(c.Department))
            .Select(c => c.ProfessorId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet();

        foreach (var prof in _workspace.Professors.OrderBy(p => SortNumericFirst(p.Id)))
        {
            ProfessorItems.Add(new ProfessorItem
            {
                Professor = prof,
                HeaderUnavailableSlots = FormatUnavailableSlots(prof.UnavailableSlots),
                HeaderUnavailableRooms = FormatNames(prof.UnavailableRooms, roomNames),
                IsImportedFromExcel = importedProfIds.Contains(prof.Id),
            });
        }
    }

    private void RebuildRoomItems()
    {
        RoomItems.Clear();
        var importedRoomIds = _workspace.Courses
            .SelectMany(c => c.FixedRooms)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet();

        foreach (var room in _workspace.Rooms.OrderBy(r => SortNumericFirst(r.Id)))
        {
            RoomItems.Add(new RoomItem
            {
                Room = room,
                IsImportedFromExcel = importedRoomIds.Contains(room.Id),
            });
        }
    }

    private void RebuildCrossManager(bool clearCandidateSelection = false)
    {
        var selected = clearCandidateSelection
            ? new HashSet<string>()
            : CrossCandidateItems.Where(item => item.IsChecked).Select(item => item.Id).ToHashSet();
        var groups = CourseBaseGroups();

        CrossGroupItems.Clear();
        foreach (var cross in _workspace.CrossGroups.OrderBy(g => g.Id))
        {
            var names = cross.BaseIds.Select(id =>
            {
                var name = groups.TryGetValue(id, out var courses) && courses.Count > 0 ? courses[0].Name : "?";
                return $"{id}({name})";
            });
            CrossGroupItems.Add(new CrossGroupListItem(cross.Id, $"{cross.Id}: {string.Join(" ↔ ", names)}"));
        }

        CrossCandidateItems.Clear();
        CrossCandidateGradeGroups.Clear();
        foreach (var gradeGroup in groups
            .OrderBy(g => g.Value[0].Grade)
            .ThenBy(g => SortNumericFirst(g.Key))
            .GroupBy(g => g.Value[0].Grade))
        {
            var items = new ObservableCollection<CheckListItem>();
            foreach (var (baseId, courses) in gradeGroup)
            {
                var rep = courses[0];
                var item = new CheckListItem(
                    baseId,
                    $"{rep.Name} (분반 {courses.Count}개, {rep.HoursPerWeek}h)",
                    selected.Contains(baseId));
                CrossCandidateItems.Add(item);
                items.Add(item);
            }
            CrossCandidateGradeGroups.Add(new CrossCandidateGradeGroup($"{gradeGroup.Key}학년", items));
        }
    }

    private Dictionary<string, List<Course>> CourseBaseGroups() => _workspace.Courses
        .GroupBy(c => DomainHelpers.BaseId(c.Id))
        .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Section).ToList());

    private static string DisplayName(string id, IReadOnlyDictionary<string, string> names) =>
        string.IsNullOrWhiteSpace(id) ? "-" : names.TryGetValue(id, out var name) ? name : id;

    private static string FormatNames(IReadOnlyList<string> ids, IReadOnlyDictionary<string, string> names) =>
        ids.Count == 0 ? "-" : string.Join(", ", ids.Select(id => DisplayName(id, names)));

    private static string RoomDisplayLabel(Room room)
    {
        var parts = new List<string>();
        if (room.IsLab) parts.Add("실습실");
        if (room.Capacity > 0) parts.Add($"{room.Capacity}명");

        return parts.Count == 0 ? room.Name : $"{room.Name} ({string.Join(", ", parts)})";
    }

    private static (int Kind, int Number, string Text) SortNumericFirst(string id) =>
        int.TryParse(id, out var number) ? (0, number, id) : (1, 0, id);

    private static List<int> EffectiveBlocks(Course course) =>
        course.BlockStructure.Count > 0 ? course.BlockStructure.ToList() : new List<int> { course.HoursPerWeek };

    private static string FormatBlocks(Course course) => string.Join("+", EffectiveBlocks(course));

    private string FormatCrossGroups(string baseId)
    {
        var partners = _workspace.CrossGroups
            .Where(g => g.BaseIds.Contains(baseId))
            .SelectMany(g => g.BaseIds)
            .Where(id => id != baseId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        return partners.Count == 0 ? "-" : string.Join(", ", partners);
    }

    private static string FormatFixedTimes(IEnumerable<Course> sections)
    {
        var labels = sections
            .Where(s => s.FixedSlots.Count > 0)
            .Select(s => $"{SectionLetter(s.Section)}분반 {FormatSlotRuns(s.FixedSlots)}")
            .ToList();
        return labels.Count == 0 ? "-" : string.Join(" / ", labels);
    }

    private static string FormatSlotRuns(IReadOnlyList<TimeSlot> slots)
    {
        var ordered = slots.OrderBy(s => s.Day).ThenBy(s => s.Period).ToList();
        var parts = new List<string>();
        int i = 0;
        while (i < ordered.Count)
        {
            var day = ordered[i].Day;
            var start = ordered[i].Period;
            var end = start;
            i++;
            while (i < ordered.Count && ordered[i].Day == day && ordered[i].Period == end + 1)
            {
                end = ordered[i].Period;
                i++;
            }
            parts.Add($"{DayName(day)} {8 + start:00}:00~{9 + end:00}:00");
        }
        return string.Join(", ", parts);
    }

    private static string FormatUnavailableSlots(IReadOnlyList<TimeSlot> slots)
    {
        if (slots.Count == 0) return "-";
        return string.Join(", ", slots
            .OrderBy(s => s.Day)
            .ThenBy(s => s.Period)
            .Select(s => $"{DayName(s.Day)} {s.Period}교시 ({8 + s.Period:00}:00~)"));
    }

    private static string DayName(int day) => day switch
    {
        0 => "월",
        1 => "화",
        2 => "수",
        3 => "목",
        4 => "금",
        _ => "?",
    };

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
            StatusMessage = ToUserProgressMessage(p);
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
                PerSolveTimeSec = UseAdvancedPerSolveTimeSec
                    ? Math.Max(1, PerSolveTimeSec)
                    : new DiverseSolverOptions().PerSolveTimeSec,
                UseSc01 = UseSc01,
                UseSc02 = UseSc02,
                UseSc03 = UseSc03,
            };
            var result = await _solver.SolveAsync(_workspace, options, progress, _cts.Token);
            var ranked = _solver.Rank(_workspace, result, topM: 10);
            RankedResults.Clear();
            foreach (var r in ranked) RankedResults.Add(r);
            StatusMessage = result.Status switch
            {
                "INFEASIBLE" => InfeasibleMessage(),
                "UNKNOWN" when ranked.Count == 0 => UnknownMessage(),
                _ when ranked.Count == 0 => NoSolutionMessage(result.Status),
                _ => $"완료: {result.Status}, {result.Solutions.Count}개 해, 미리보기 {ranked.Count}개",
            };
            IsSolveComplete = ranked.Count > 0;
            if (ranked.Count > 0)
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

    private static string ToUserProgressMessage(SolverProgress progress)
    {
        var message = progress.Message
            .Replace("SC-01", "월/금 시간 선호")
            .Replace("SC-02", "교수 요일 선호")
            .Replace("SC-03", "수업 간격 선호")
            .Replace("Phase 1A", "선호 조건 확인")
            .Replace("Phase 1B", "선호 조건 확인")
            .Replace("Phase 1C", "선호 조건 확인")
            .Replace("Phase 2", "시간표 후보 탐색")
            .Replace("opt=", "최적값=");
        return $"{message}";
    }

    private string InfeasibleMessage()
    {
        var reasons = new List<string>();
        foreach (var prof in _workspace.Professors)
            if (_workspace.Rooms.Count > 0 && prof.UnavailableRooms.Count >= _workspace.Rooms.Count)
                reasons.Add($"{prof.Name} 교수의 불가 강의실이 전체 선택됨");
        foreach (var course in _workspace.Courses)
            if (_workspace.Rooms.Count > 0 && course.UnavailableRooms.Count >= _workspace.Rooms.Count)
                reasons.Add($"{course.Name} 과목의 불가 강의실이 전체 선택됨");
        foreach (var course in _workspace.Courses)
            if (course.BlockStructure.Count > 0 && course.BlockStructure.Sum() != course.HoursPerWeek)
                reasons.Add($"{course.Name} 과목의 블록구조 합이 주당 수업시간과 다름");

        return reasons.Count == 0
            ? "해를 찾지 못했습니다. 불가 시간, 불가 강의실, 시간 고정, Cross 설정을 줄여보세요."
            : $"해를 찾지 못했습니다. 원인: {string.Join(" / ", reasons.Take(3))}";
    }

    private string UnknownMessage() =>
        "시간 초과(UNKNOWN): 제한 시간 안에 해를 찾거나 불가능함을 확정하지 못했습니다. " +
        "고급 설정의 '한 번 탐색 제한 시간'을 켜고 값을 늘려 다시 시도해 보세요.";

    private string NoSolutionMessage(string status) =>
        $"해를 찾지 못했습니다({status}). 불가 시간, 불가 강의실, 시간 고정, Cross 설정을 줄이거나 제한 시간을 늘려보세요.";
}
