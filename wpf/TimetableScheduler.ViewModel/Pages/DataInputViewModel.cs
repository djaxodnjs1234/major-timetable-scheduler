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
    IReadOnlyList<SavedManualCrossLinkRow> ManualCrossLinks,
    string? SavedTimetableId);

public sealed record CrossGroupListItem(string Id, string Display);

public sealed partial class CrossCandidateItemViewModel : ObservableObject
{
    public CheckListItem BaseItem { get; }
    public string Id => BaseItem.Id;
    public string Display => BaseItem.Display;

    public bool IsChecked
    {
        get => BaseItem.IsChecked;
        set => BaseItem.IsChecked = value;
    }

    [ObservableProperty]
    private bool isEnabled = true;

    public CrossCandidateItemViewModel(CheckListItem baseItem)
    {
        BaseItem = baseItem;
        if (baseItem is System.ComponentModel.INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CheckListItem.IsChecked) || e.PropertyName == "IsChecked")
                {
                    OnPropertyChanged(nameof(IsChecked));
                }
            };
        }
    }
}

public sealed record CrossCandidateGradeGroup(string Header, ObservableCollection<CrossCandidateItemViewModel> Items);

public sealed partial class ProfessorItem : ObservableObject
{
    [ObservableProperty]
    private Professor professor = new();

    public string HeaderId => Professor.Id;
    public string HeaderName => Professor.Name;
    public string HeaderUnavailableSlots { get; init; } = "없음";
    public bool IsImportedFromExcel { get; init; }

    [ObservableProperty]
    private bool isEditing;
}

public sealed partial class RoomItem : ObservableObject
{
    [ObservableProperty]
    private Room room = new();

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
    public string HeaderCourseType { get; init; } = "";
    public string HeaderGrade { get; init; } = "";
    public string HeaderHours { get; init; } = "";
    public string HeaderSectionInfo { get; init; } = "";
    public string HeaderProfessor { get; init; } = "";
    public string HeaderBlockStructure { get; init; } = "";
    public string HeaderUnavailableRooms { get; init; } = "";
    public string HeaderFixedRooms { get; init; } = "";
    public string HeaderCoteachProfessors { get; init; } = "";
    public string HeaderFixedTimes { get; init; } = "";
    public string HeaderFixedMarker { get; init; } = "";
    public string HeaderCrossGroups { get; init; } = "";
    public bool IsImportedFromExcel { get; init; }
    /// <summary>All sections in this group (N>1 for grouped, 1 for individual fixed).</summary>
    [ObservableProperty]
    private List<Course> sections = new();

    public bool IsFixedIndividual => Sections.Count == 1 && Sections[0].IsFixed;
    public string ReadOnlyProfessor => string.IsNullOrWhiteSpace(HeaderProfessor) ? "없음" : HeaderProfessor;
    public string ReadOnlyBlockStructure => HeaderBlockStructure;
    public string ReadOnlyUnavailableRooms => string.IsNullOrWhiteSpace(HeaderUnavailableRooms) ? "없음" : HeaderUnavailableRooms;
    public string ReadOnlyFixedRooms => string.IsNullOrWhiteSpace(HeaderFixedRooms) ? "없음" : HeaderFixedRooms;
    public string ReadOnlyCoteachProfessors => string.IsNullOrWhiteSpace(HeaderCoteachProfessors) ? "없음" : HeaderCoteachProfessors;
    public string ReadOnlyFixedTimes => string.IsNullOrWhiteSpace(HeaderFixedTimes) ? "없음" : HeaderFixedTimes;

    [ObservableProperty]
    private bool isEditing;

    [ObservableProperty]
    private FixedSlotEditorViewModel? fixedSlotEditor;

    partial void OnSectionsChanged(List<Course> value) => OnPropertyChanged(nameof(IsFixedIndividual));
}

public sealed record FixedTimeOverlapInfo(
    string ExistingCourseId,
    string ExistingCourseName,
    int ExistingSection,
    int Day,
    int Period);

public sealed partial class DataInputViewModel : PageViewModelBase
{
    private WorkspaceService _workspace;
    private readonly WorkspaceService _globalWorkspace;
    private readonly SolverService _solver;
    private CancellationTokenSource? _cts;
    private EventHandler? _workspaceChangedHandler;
    private List<string> _selectedCrossCandidateIds = new();

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
    public ObservableCollection<CrossCandidateItemViewModel> CrossCandidateItems { get; } = new();
    public ObservableCollection<CrossCandidateGradeGroup> CrossCandidateGradeGroups { get; } = new();

    public string LastCourseGroupWarning { get; private set; } = "";

    public IReadOnlyList<AcademicLevel> GradeOptions { get; } = AcademicLevels.All;
    public int[] HourOptions { get; } = { 1, 2, 3, 4, 5 };
    public string[] CourseTypeOptions { get; } = { "전필", "전선", "교양" };
    public string[] BlockStructureOptions { get; } = GenerateBlockStructureOptions(3).ToArray();

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
                if (string.IsNullOrWhiteSpace(NewName))
                {
                    StatusMessage = "IE-002: 교수 관리 > 추가: 교수명을 입력해야 추가할 수 있습니다.";
                    return;
                }
                _workspace.AddProfessor(new Professor { Id = NextProfessorId(), Name = NewName.Trim() });
                break;
            case InputCategory.Course:
                if (string.IsNullOrWhiteSpace(NewName))
                {
                    StatusMessage = "IE-001: 교과목 관리 > 추가: 과목명을 입력해야 추가할 수 있습니다.";
                    return;
                }
                _workspace.AddCourse(new Course
                {
                    Id = NextCourseBaseId(), Name = NewName.Trim(),
                    Grade = 1, HoursPerWeek = 3,
                    BlockStructure = DefaultBlockStructureForHours(3),
                    CourseType = "전선",
                    UnavailableRooms = new List<string>(),
                });
                break;
            case InputCategory.Room:
                if (string.IsNullOrWhiteSpace(NewName))
                {
                    StatusMessage = "IE-003: 강의실 관리 > 추가: 강의실명을 입력해야 추가할 수 있습니다.";
                    return;
                }
                _workspace.AddRoom(new Room { Id = NextRoomId(), Name = NewName.Trim() });
                break;
        }
        NewId = "";
        NewName = "";
        StatusMessage = "";
    }

    [RelayCommand]
    private void AddCross()
    {
        if (_selectedCrossCandidateIds.Count != 2)
        {
            CrossStatusMessage = "Cross는 과목 2개만 선택할 수 있습니다.";
            return;
        }
        var chosen = _selectedCrossCandidateIds.ToList();

        var groups = CourseBaseGroups();
        if (chosen.Any(id => !groups.TryGetValue(id, out var courses) || courses.Count < 2))
        {
            CrossStatusMessage = "Cross는 분반이 2개 이상인 과목만 선택할 수 있습니다.";
            return;
        }

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

        var blockTexts = chosen
            .Select(id => FormatBlockStructure(EffectiveBlocks(groups[id][0])))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (blockTexts.Count > 1)
        {
            var detail = string.Join(", ", chosen.Select(id =>
                $"{id}: {FormatBlockStructure(EffectiveBlocks(groups[id][0]))}"));
            CrossStatusMessage = $"Cross로 묶으려면 블록구조가 같아야 합니다. ({detail})";
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
        if (TryDeleteItem(SelectedItem))
            SelectedItem = null;
    }

    private bool TryDeleteItem(object? item)
    {
        try
        {
            switch (item)
            {
                case Professor p:
                    if (IsProfessorUsedByCurrentResults(p.Id))
                        return BlockDelete("이 교수는 현재 생성된 시간표에서 사용 중이므로 삭제할 수 없습니다.");
                    _workspace.DeleteProfessor(p.Id);
                    return true;
                case Course c:
                    if (IsCourseUsedByCurrentResults(c.Id))
                        return BlockDelete("이 교과목은 현재 생성된 시간표에서 사용 중이므로 삭제할 수 없습니다.");
                    _workspace.DeleteCourse(c);
                    return true;
                case Room r:
                    if (IsRoomUsedByCurrentResults(r.Id))
                        return BlockDelete("이 강의실은 현재 생성된 시간표에서 사용 중이므로 삭제할 수 없습니다.");
                    _workspace.DeleteRoom(r.Id);
                    return true;
                default:
                    return false;
            }
        }
        catch (InvalidOperationException ex)
        {
            return BlockDelete(ex.Message);
        }
    }

    private bool BlockDelete(string message)
    {
        DeleteBlocked?.Invoke(this, message);
        return false;
    }

    private bool IsProfessorUsedByCurrentResults(string professorId)
    {
        if (RankedResults.Count == 0)
            return false;
        var taughtCourseIds = _workspace.Courses
            .Where(c => string.Equals(c.ProfessorId, professorId, StringComparison.Ordinal)
                || c.CoteachProfs.Contains(professorId, StringComparer.Ordinal))
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);
        return RankedResults
            .SelectMany(r => r.Assignment)
            .Any(a => taughtCourseIds.Contains(a.CourseId));
    }

    private bool IsRoomUsedByCurrentResults(string roomId) =>
        RankedResults
            .SelectMany(r => r.Assignment)
            .Any(a => string.Equals(a.RoomId, roomId, StringComparison.Ordinal));

    private bool IsCourseUsedByCurrentResults(string courseId) =>
        RankedResults
            .SelectMany(r => r.Assignment)
            .Any(a => string.Equals(a.CourseId, courseId, StringComparison.Ordinal));

    [RelayCommand]
    private void SaveSelected()
    {
        switch (SelectedItem)
        {
            case Professor p:
                if (string.IsNullOrWhiteSpace(p.Name))
                {
                    StatusMessage = $"IE-002: {ProfessorInputLocation(p)}: 교수명을 입력해야 저장할 수 있습니다.";
                    return;
                }
                _workspace.UpdateProfessor(p);
                break;
            case Course c:
                if (string.IsNullOrWhiteSpace(c.Name))
                {
                    StatusMessage = $"IE-001: {CourseInputLocation(c)}: 과목명을 입력해야 저장할 수 있습니다.";
                    return;
                }
                _workspace.UpdateCourse(c);
                break;
            case Room r:
                if (string.IsNullOrWhiteSpace(r.Name))
                {
                    StatusMessage = $"IE-003: {RoomInputLocation(r)}: 강의실명을 입력해야 저장할 수 있습니다.";
                    return;
                }
                _workspace.UpdateRoom(r);
                break;
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
        if (string.IsNullOrWhiteSpace(item.Professor.Name))
        {
            StatusMessage = $"IE-002: {ProfessorInputLocation(item.Professor)}: 교수명을 입력해야 저장할 수 있습니다.";
            return;
        }
        _workspace.UpdateProfessor(item.Professor);
        item.IsEditing = false;
    }

    [RelayCommand]
    private void CancelProfessor(ProfessorItem item)
    {
        if (item is null) return;
        var savedProfessor = _workspace.Professors.FirstOrDefault(professor => professor.Id == item.Professor.Id);
        if (savedProfessor == null) return;

        item.Professor = CloneProfessor(savedProfessor);
        item.IsEditing = false;
    }

    [RelayCommand]
    private void DeleteProfessorItem(ProfessorItem item)
    {
        if (item is null) return;
        TryDeleteItem(item.Professor);
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
        if (string.IsNullOrWhiteSpace(item.Room.Name))
        {
            StatusMessage = $"IE-003: {RoomInputLocation(item.Room)}: 강의실명을 입력해야 저장할 수 있습니다.";
            return;
        }
        _workspace.UpdateRoom(item.Room);
        item.IsEditing = false;
    }

    [RelayCommand]
    private void CancelRoom(RoomItem item)
    {
        if (item is null) return;
        var savedRoom = _workspace.Rooms.FirstOrDefault(room => room.Id == item.Room.Id);
        if (savedRoom == null) return;

        item.Room = CloneRoom(savedRoom);
        item.IsEditing = false;
    }

    [RelayCommand]
    private void DeleteRoomItem(RoomItem item)
    {
        if (item is null) return;
        TryDeleteItem(item.Room);
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
        var saveError = FirstCourseSaveError(item.Sections);
        if (saveError != null)
        {
            StatusMessage = saveError.ToString();
            return;
        }
        LastCourseGroupWarning = "";
        OnPropertyChanged(nameof(LastCourseGroupWarning));
        PromoteMissingSharedValues(item.Sections, promoteListValues: false);
        DetectProfessorConflict("SaveGroup", item.BaseId, item.Sections);
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
                if (!sec.IsFixed)
                    sec.FixedSlots.Clear();
                sec.FixedRooms = new List<string>(rep.FixedRooms);
                sec.UnavailableRooms = new List<string>(rep.UnavailableRooms);
                sec.BlockStructure = new List<int>(rep.BlockStructure);
                sec.CoteachProfs = new List<string>(rep.CoteachProfs);
                // FixedSlots intentionally not copied — each section has its own slots
            }
            else if (!sec.IsFixed)
            {
                sec.FixedSlots.Clear();
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
        {
            if (!TryDeleteItem(sec))
                return;
        }
    }

    /// <summary>Save a single fixed section (IsFixedIndividual=true).</summary>
    [RelayCommand]
    private void SaveSection(CourseGroupItem item)
    {
        if (item is null || item.Sections.Count != 1) return;
        var saveError = FirstCourseSaveError(item.Sections);
        if (saveError != null)
        {
            StatusMessage = saveError.ToString();
            return;
        }
        _workspace.UpdateCourse(item.Sections[0]);
        item.IsEditing = false;
    }

    [RelayCommand]
    private void CancelGroup(CourseGroupItem item)
    {
        if (item is null) return;

        var savedSections = _workspace.Courses
            .Where(course => DomainHelpers.BaseId(course.Id) == item.BaseId)
            .OrderBy(course => course.Section)
            .Select(CloneCourse)
            .ToList();
        if (savedSections.Count == 0) return;

        item.Sections = savedSections;
        item.FixedSlotEditor = FixedSlotEditorViewModel.Build(item, savedSections[0].IsFixed);
        item.IsEditing = false;
    }

    private TimetableDiagnostic? FirstCourseSaveError(IReadOnlyList<Course> sections)
    {
        var roomIds = _workspace.Rooms.Select(room => room.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var course in sections)
        {
            var courseName = CourseInputLocation(course);
            if (string.IsNullOrWhiteSpace(course.Name))
                return new TimetableDiagnostic("IE-001", $"{courseName}: 과목명을 입력해야 저장할 수 있습니다.");

            if (course.BlockStructure.Count > 0 && course.BlockStructure.Sum() != course.HoursPerWeek)
                return new TimetableDiagnostic("IE-007", $"{courseName} 블록구조 합계가 주당 수업시간과 일치하지 않습니다.");

            if (course.IsFixed && course.FixedSlots.Count == 0)
                return new TimetableDiagnostic("IE-010", $"{courseName} 시간고정이 켜졌지만 고정 시간이 비어 있습니다.");

            if (course.IsFixed && course.FixedSlots.Any(slot => slot.Period == Constants.LunchPeriod))
                return new TimetableDiagnostic("IE-011", $"{courseName} 고정 시간이 점심시간 5교시를 포함합니다.");

            if (course.IsFixed && course.FixedSlots.Count > 0 && course.FixedSlots.Count != course.HoursPerWeek)
                return new TimetableDiagnostic("IE-012", $"{courseName} 고정 시간 개수가 주당 수업시간과 다릅니다.");

            if (course.IsFixed && course.FixedSlots.Count > 0 && !FixedSlotsMatchBlocks(course))
                return new TimetableDiagnostic("IE-013", $"{courseName} 고정 시간이 블록구조와 맞지 않습니다.");

            var overlapRooms = course.FixedRooms.Intersect(course.UnavailableRooms, StringComparer.Ordinal).ToList();
            if (overlapRooms.Count > 0)
                return new TimetableDiagnostic("IE-020", $"{courseName} 고정강의실과 불가강의실이 겹칩니다: {string.Join(", ", overlapRooms)}");

            if (_workspace.Rooms.Count > 0)
            {
                var missingFixedRoom = course.FixedRooms.FirstOrDefault(roomId => !roomIds.Contains(roomId));
                if (missingFixedRoom != null)
                    return new TimetableDiagnostic("IE-021", $"{courseName} 고정강의실({missingFixedRoom})을 찾을 수 없습니다.");

                var missingUnavailableRoom = course.UnavailableRooms.FirstOrDefault(roomId => !roomIds.Contains(roomId));
                if (missingUnavailableRoom != null)
                    return new TimetableDiagnostic("IE-022", $"{courseName} 불가강의실({missingUnavailableRoom})을 찾을 수 없습니다.");
            }

            if (_workspace.Rooms.Count > 0 && course.UnavailableRooms.Intersect(roomIds, StringComparer.Ordinal).Count() >= _workspace.Rooms.Count)
                return new TimetableDiagnostic("IE-019", $"{courseName} 과목은 모든 강의실이 불가로 설정되어 있습니다.");
        }

        return null;
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
        TryDeleteItem(item.Sections[0]);
    }

    /// <summary>Add the next-numbered section to the group, copying shared fields from Sections[0].</summary>
    [RelayCommand]
    private void AddSection(CourseGroupItem item)
    {
        if (item is null || item.Sections.Count == 0) return;
        ClearFixedTimeForSectionAdjustment(item);
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
        ClearFixedTimeForSectionAdjustment(item);
        var last = item.Sections.OrderByDescending(s => s.Section).First();
        _workspace.DeleteCourse(last);
    }

    private void ClearFixedTimeForSectionAdjustment(CourseGroupItem item)
    {
        if (!item.Sections.Any(section => section.IsFixed || section.FixedSlots.Count > 0)) return;

        foreach (var section in item.Sections)
        {
            section.IsFixed = false;
            section.FixedSlots.Clear();
            _workspace.UpdateCourse(section);
        }
    }

    [ObservableProperty]
    private int totalSolutions = 3;

    [ObservableProperty]
    private int timeLimitSec = new DiverseSolverOptions().TimeLimitSec;

    [ObservableProperty]
    private bool considerRetakeStudents;

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
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isSolving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoToSelectionCommand))]
    private bool isSolveComplete;

    public ObservableCollection<RankedSolution> RankedResults { get; } = new();

    public event EventHandler<IReadOnlyList<RankedSolution>>? SolveCompleted;
    public event EventHandler? GoToSelectionRequested;
    public event EventHandler? GoToManualRequested;
    public event EventHandler? BackRequested;
    public event EventHandler<string>? DeleteBlocked;

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanGoToSelection))]
    private void GoToSelection()
    {
        GoToSelectionRequested?.Invoke(this, EventArgs.Empty);
    }
    private bool CanGoToSelection() => IsSolveComplete;

    /// <summary>
    /// Skip the solver and hand the existing timetable's base assignments straight to
    /// manual editing. Only valid in existing-timetable mode (a base layout exists).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoToManual))]
    private void GoToManual()
    {
        GoToManualRequested?.Invoke(this, EventArgs.Empty);
    }
    private bool CanGoToManual() => IsExistingMode;

    /// <summary>The base assignments + saved manual cross links for handing off to manual editing.</summary>
    public ManualEditHandoff? BuildEditHandoff()
    {
        if (_editBaseAssignments == null) return null;
        var solution = new RankedSolution(_editBaseAssignments, new SolutionScore(0, 0, 0, 0));
        return new ManualEditHandoff(solution, _editBaseManualCrossLinks, _editBaseId);
    }

    public AppData CurrentSnapshot() => _workspace.SchedulingSnapshot();

    public override void OnNavigatedTo()
    {
        RebuildProfessorItems();
        RebuildCourseGroups();
        RebuildRoomItems();
        RebuildCrossManager();
    }

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
        _editBaseId = null;
        _editBaseName = "";
        SelectedCategory = InputCategory.Course;
    }

    /// <summary>
    /// Enter "existing timetable" mode — edits an in-memory session workspace seeded
    /// from the timetable's constraint snapshot. The global workspace is untouched.
    /// </summary>
    public void LoadForExistingTimetable(SavedTimetableRecord record)
    {
        var snapshot = SavedTimetableSnapshotResolver.Resolve(record.SnapshotJson);
        SwitchWorkspace(WorkspaceService.CreateSession(snapshot));
        IsExistingMode = true;
        SelectedCategory = InputCategory.Course;
        _editBaseAssignments = record.Assignments
            .Select(r => new SolutionAssignment(r.CourseId, r.Day, r.Period, r.RoomId, r.AssignmentId ?? ""))
            .ToList();
        _editBaseManualCrossLinks = (record.ManualCrossLinks ?? Array.Empty<SavedManualCrossLinkRow>())
            .ToList();
        _editBaseId = record.Id;
        _editBaseName = record.Name;
    }

    private IReadOnlyList<SolutionAssignment>? _editBaseAssignments;
    private IReadOnlyList<SavedManualCrossLinkRow> _editBaseManualCrossLinks = Array.Empty<SavedManualCrossLinkRow>();
    private string? _editBaseId;
    private string _editBaseName = "";

    /// <summary>Name of the timetable being re-edited (for the manual-edit save field).</summary>
    public string EditBaseName => _editBaseName;

    private static readonly string[] SectionLetters = { "A","B","C","D","E","F" };

    private static string SectionLetter(int section) =>
        section >= 1 && section <= SectionLetters.Length
            ? SectionLetters[section - 1] : section.ToString();

    public FixedTimeOverlapInfo? FindFixedTimeOverlap(
        CourseGroupItem item,
        IReadOnlyList<IReadOnlyList<TimeSlot>>? candidateFixedSlots = null)
    {
        if (item.Sections.Count == 0 || !item.Sections[0].IsFixed)
            return null;

        var candidateSections = item.Sections
            .Select((section, index) => new
            {
                Section = section,
                Slots = candidateFixedSlots != null && index < candidateFixedSlots.Count
                    ? candidateFixedSlots[index].ToList()
                    : section.FixedSlots.ToList(),
            })
            .ToList();

        static (string Id, int Section) KeyOf(Course course) => (course.Id, course.Section);

        foreach (var current in candidateSections)
        {
            foreach (var other in _workspace.Courses)
            {
                if (KeyOf(other) == KeyOf(current.Section))
                    continue;
                if (!other.IsFixed || other.FixedSlots.Count == 0)
                    continue;

                var overlap = current.Slots
                    .FirstOrDefault(slot => other.FixedSlots.Contains(slot));
                if (current.Slots.Contains(overlap))
                {
                    return new FixedTimeOverlapInfo(
                        other.Id,
                        other.Name,
                        other.Section,
                        overlap.Day,
                        overlap.Period);
                }
            }
        }

        for (int i = 0; i < candidateSections.Count; i++)
        {
            for (int j = i + 1; j < candidateSections.Count; j++)
            {
                var overlap = candidateSections[i].Slots
                    .FirstOrDefault(slot => candidateSections[j].Slots.Contains(slot));
                if (candidateSections[i].Slots.Contains(overlap))
                {
                    var other = candidateSections[j].Section;
                    return new FixedTimeOverlapInfo(
                        other.Id,
                        other.Name,
                        other.Section,
                        overlap.Day,
                        overlap.Period);
                }
            }
        }

        return null;
    }

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
            var sections = g.OrderBy(c => c.Section).Select(CloneCourse).ToList();

            PromoteMissingSharedValues(sections);
            DetectProfessorConflict("RebuildCourseGroups", g.Key, sections);
            var rep = sections[0];
            var fixedAny = sections.Any(c => c.IsFixed);
            var secPart = sections.Count > 1
                ? $"  ({string.Join("·", sections.Select(s => SectionLetter(s.Section)))}분반)"
                : "";
            var fixedPart = fixedAny ? "  ★" : "";
            var label = $"{g.Key}  {rep.Name}  {AcademicLevels.DisplayName(rep.Grade)}  {rep.HoursPerWeek}h{secPart}{fixedPart}";
            var item = new CourseGroupItem
            {
                BaseId = g.Key,
                DisplayLabel = label,
                HeaderCode = g.Key,
                HeaderName = rep.Name,
                HeaderCourseType = rep.CourseType,
                HeaderGrade = AcademicLevels.DisplayName(rep.Grade),
                HeaderHours = $"{rep.HoursPerWeek}시간",
                HeaderSectionInfo = $"{sections.Count}개",
                HeaderProfessor = CourseDisplayName(rep.ProfessorId, profNames),
                HeaderBlockStructure = FormatBlocks(rep),
                HeaderUnavailableRooms = FormatCourseNames(rep.UnavailableRooms, roomNames),
                HeaderFixedRooms = FormatCourseNames(rep.FixedRooms, roomNames),
                HeaderCoteachProfessors = FormatCourseNames(rep.CoteachProfs, profNames),
                HeaderFixedTimes = FormatFixedTimes(sections),
                HeaderFixedMarker = fixedAny ? "★" : "",
                HeaderCrossGroups = FormatCrossGroups(g.Key),
                IsImportedFromExcel = sections.Any(s => !string.IsNullOrWhiteSpace(s.Department)),
                Sections = sections,
            };
            item.FixedSlotEditor = FixedSlotEditorViewModel.Build(item, rep.IsFixed);
            CourseGroups.Add(item);
        }
    }

    private void PromoteMissingSharedValues(IReadOnlyList<Course> sections, bool promoteListValues = true)
    {
        if (sections.Count == 0) return;
        var rep = sections[0];

        var professorId = FirstNonEmpty(sections.Select(s => s.ProfessorId));
        if (string.IsNullOrWhiteSpace(rep.ProfessorId) && professorId != null)
            rep.ProfessorId = professorId;

        var courseType = FirstNonEmpty(sections.Select(s => s.CourseType));
        if (string.IsNullOrWhiteSpace(rep.CourseType) && courseType != null)
            rep.CourseType = courseType;

        if (!promoteListValues)
            return;

        if (rep.UnavailableRooms.Count == 0)
        {
            var unavailableRooms = sections.FirstOrDefault(s => s.UnavailableRooms.Count > 0)?.UnavailableRooms;
            if (unavailableRooms != null)
                rep.UnavailableRooms = new List<string>(unavailableRooms);
        }

        if (rep.FixedRooms.Count == 0)
        {
            var fixedRooms = sections.FirstOrDefault(s => s.FixedRooms.Count > 0)?.FixedRooms;
            if (fixedRooms != null)
                rep.FixedRooms = new List<string>(fixedRooms);
        }

        if (rep.CoteachProfs.Count == 0)
        {
            var coteachProfs = sections.FirstOrDefault(s => s.CoteachProfs.Count > 0)?.CoteachProfs;
            if (coteachProfs != null)
                rep.CoteachProfs = new List<string>(coteachProfs);
        }
    }

    private void DetectProfessorConflict(string context, string baseId, IReadOnlyList<Course> sections)
    {
        var nonEmptyProfessorIds = sections
            .Select(s => s.ProfessorId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (nonEmptyProfessorIds.Count <= 1) return;

        LastCourseGroupWarning = $"Inconsistent ProfessorId values in {baseId}: {string.Join(", ", nonEmptyProfessorIds)}";
        OnPropertyChanged(nameof(LastCourseGroupWarning));
    }

    private static string? FirstNonEmpty(IEnumerable<string> values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static Course CloneCourse(Course src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Grade = src.Grade,
        HoursPerWeek = src.HoursPerWeek,
        CourseType = src.CourseType,
        ProfessorId = src.ProfessorId,
        Section = src.Section,
        Department = src.Department,
        FixedRooms = new List<string>(src.FixedRooms),
        UnavailableRooms = new List<string>(src.UnavailableRooms),
        BlockStructure = new List<int>(src.BlockStructure),
        IsFixed = src.IsFixed,
        FixedSlots = new List<TimeSlot>(src.FixedSlots),
        CoteachProfs = new List<string>(src.CoteachProfs),
    };

    private static Professor CloneProfessor(Professor src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        UnavailableSlots = new List<TimeSlot>(src.UnavailableSlots),
        AllowedRooms = new List<string>(src.AllowedRooms),
        UnavailableRooms = new List<string>(src.UnavailableRooms),
    };

    private static Room CloneRoom(Room src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        IsLab = src.IsLab,
        Capacity = src.Capacity,
        IsImportedFromExcel = src.IsImportedFromExcel,
    };

    private void RebuildProfessorItems()
    {
        ProfessorItems.Clear();
        var importedProfIds = _workspace.Courses
            .Where(c => !string.IsNullOrWhiteSpace(c.Department))
            .Select(c => c.ProfessorId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet();

        foreach (var prof in _workspace.Professors.OrderBy(p => SortNumericFirst(p.Id)))
        {
            var editProf = CloneProfessor(prof);
            ProfessorItems.Add(new ProfessorItem
            {
                Professor = editProf,
                HeaderUnavailableSlots = FormatUnavailableSlots(editProf.UnavailableSlots),
                IsImportedFromExcel = importedProfIds.Contains(editProf.Id),
            });
        }
    }

    private void RebuildRoomItems()
    {
        RoomItems.Clear();
        var importedRoomIds = _workspace.ImportedRoomIds.Count > 0
            ? _workspace.ImportedRoomIds.ToHashSet(StringComparer.Ordinal)
            : _workspace.Courses
                .SelectMany(c => c.FixedRooms)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

        foreach (var room in _workspace.Rooms.OrderBy(r => SortNumericFirst(r.Id)))
        {
            var editRoom = CloneRoom(room);
            editRoom.IsImportedFromExcel = room.IsImportedFromExcel || importedRoomIds.Contains(room.Id);
            RoomItems.Add(new RoomItem
            {
                Room = editRoom,
                IsImportedFromExcel = editRoom.IsImportedFromExcel,
            });
        }
    }

    private void RebuildCrossManager(bool clearCandidateSelection = false)
    {
        _selectedCrossCandidateIds = clearCandidateSelection
            ? new List<string>()
            : CrossCandidateItems.Where(item => item.IsChecked).Select(item => item.Id).ToList();
        if (_selectedCrossCandidateIds.Count > 2)
            _selectedCrossCandidateIds = _selectedCrossCandidateIds.Take(2).ToList();
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
            var items = CheckListBinder.Bind(
                gradeGroup.ToList(),
                entry => entry.Key,
                entry => $"{entry.Value[0].Name} (분반 {entry.Value.Count}개, {entry.Value[0].HoursPerWeek}h)",
                _selectedCrossCandidateIds,
                (id, isChecked) =>
                {
                    _selectedCrossCandidateIds = CrossCandidateItems
                        .Where(item => item.IsChecked)
                        .Select(item => item.Id)
                        .ToList();

                    if (isChecked && _selectedCrossCandidateIds.Count > 2)
                    {
                        var itemToUncheck = CrossCandidateItems.FirstOrDefault(item => item.Id == id);
                        if (itemToUncheck != null)
                        {
                            itemToUncheck.IsChecked = false;
                            _selectedCrossCandidateIds.Remove(id);
                        }
                        CrossStatusMessage = "Cross는 최대 2개의 과목만 선택할 수 있습니다.";
                    }
                    else
                    {
                        CrossStatusMessage = "";
                    }
                    UpdateCrossCandidateEnabledState();
                }
            );
            
            var viewModels = items.Select(item => new CrossCandidateItemViewModel(item)).ToList();
            foreach (var vm in viewModels)
            {
                CrossCandidateItems.Add(vm);
            }
            CrossCandidateGradeGroups.Add(new CrossCandidateGradeGroup(AcademicLevels.DisplayName(gradeGroup.Key), new ObservableCollection<CrossCandidateItemViewModel>(viewModels)));
        }

        UpdateCrossCandidateEnabledState();

        if (_selectedCrossCandidateIds.Count == 2 && CrossStatusMessage == "Cross는 최대 2개의 과목만 선택할 수 있습니다.")
            CrossStatusMessage = "";
    }

    private void UpdateCrossCandidateEnabledState()
    {
        var usedInOtherCrossGroups = _workspace.CrossGroups
            .SelectMany(g => g.BaseIds)
            .ToHashSet();

        var groups = CourseBaseGroups();

        int? selectedSectionCount = null;
        int? selectedHours = null;
        int? selectedGrade = null;

        if (_selectedCrossCandidateIds.Count > 0)
        {
            var firstSelected = _selectedCrossCandidateIds.First();
            if (groups.TryGetValue(firstSelected, out var selectedCourses) && selectedCourses.Count > 0)
            {
                selectedSectionCount = selectedCourses.Count;
                selectedHours = selectedCourses[0].HoursPerWeek;
                selectedGrade = selectedCourses[0].Grade;
            }
        }

        foreach (var vm in CrossCandidateItems)
        {
            bool isUsed = usedInOtherCrossGroups.Contains(vm.Id);
            bool isSelected = _selectedCrossCandidateIds.Contains(vm.Id);
            bool hasEnoughSections = groups.TryGetValue(vm.Id, out var courses) && courses.Count >= 2;

            if (_selectedCrossCandidateIds.Count >= 2 && !isSelected)
            {
                vm.IsEnabled = false;
            }
            else if (!hasEnoughSections)
            {
                vm.IsEnabled = false;
            }
            else if (isUsed && !isSelected)
            {
                vm.IsEnabled = false;
            }
            else if (selectedSectionCount.HasValue && selectedHours.HasValue && selectedGrade.HasValue && !isSelected)
            {
                if (groups.TryGetValue(vm.Id, out courses) && courses.Count > 0)
                {
                    vm.IsEnabled = (courses.Count == selectedSectionCount.Value && courses[0].HoursPerWeek == selectedHours.Value && courses[0].Grade == selectedGrade.Value);
                }
                else
                {
                    vm.IsEnabled = false;
                }
            }
            else
            {
                vm.IsEnabled = true;
            }
        }
    }

    private Dictionary<string, List<Course>> CourseBaseGroups() => _workspace.Courses
        .GroupBy(c => DomainHelpers.BaseId(c.Id))
        .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Section).ToList());

    private static string DisplayName(string id, IReadOnlyDictionary<string, string> names) =>
        string.IsNullOrWhiteSpace(id) ? "-" : names.TryGetValue(id, out var name) ? name : id;

    private static string CourseDisplayName(string id, IReadOnlyDictionary<string, string> names) =>
        string.IsNullOrWhiteSpace(id) ? "" : names.TryGetValue(id, out var name) ? name : id;

    private static string FormatNames(IReadOnlyList<string> ids, IReadOnlyDictionary<string, string> names) =>
        ids.Count == 0 ? "-" : string.Join(", ", ids.Select(id => DisplayName(id, names)));

    private static string FormatCourseNames(IReadOnlyList<string> ids, IReadOnlyDictionary<string, string> names) =>
        ids.Count == 0 ? "" : string.Join(", ", ids.Select(id => CourseDisplayName(id, names)));

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

    private static bool FixedSlotsMatchBlocks(Course course)
    {
        var blocks = EffectiveBlocks(course).OrderBy(x => x).ToList();
        var runs = course.FixedSlots
            .GroupBy(slot => slot.Day)
            .SelectMany(dayGroup =>
            {
                var periods = dayGroup
                    .Select(slot => slot.Period)
                    .Distinct()
                    .OrderBy(period => period)
                    .ToList();
                var lengths = new List<int>();
                var index = 0;
                while (index < periods.Count)
                {
                    var length = 1;
                    while (index + length < periods.Count && periods[index + length] == periods[index] + length)
                        length++;
                    lengths.Add(length);
                    index += length;
                }
                return lengths;
            })
            .OrderBy(x => x)
            .ToList();

        return runs.SequenceEqual(blocks);
    }

    private static string FormatBlocks(Course course) => string.Join("+", EffectiveBlocks(course));

    public static IReadOnlyList<string> GenerateBlockStructureOptions(int weeklyHours) => weeklyHours switch
    {
        <= 1 => new[] { "1" },
        2 => new[] { "2" },
        3 => new[] { "1+2", "3" },
        4 => new[] { "2+2", "4" },
        5 => new[] { "1+2+2", "2+3" },
        _ => new[] { weeklyHours.ToString() },
    };

    public static string FormatBlockStructure(IEnumerable<int> blocks) => string.Join("+", blocks);

    public static bool IsValidBlockStructure(IReadOnlyList<int> blocks, int weeklyHours)
    {
        if (blocks.Count == 0) return false;
        return GenerateBlockStructureOptions(weeklyHours).Contains(FormatBlockStructure(blocks));
    }

    public static List<int> DefaultBlockStructureForHours(int weeklyHours) =>
        ParseBlockStructure(GenerateBlockStructureOptions(weeklyHours).First());

    public bool HandleCourseHoursChanged(CourseGroupItem item, int weeklyHours)
    {
        if (item.Sections.Count == 0) return false;
        var rep = item.Sections[0];

        var hoursChanged = rep.HoursPerWeek != weeklyHours;
        var previousBlocks = EffectiveBlocks(rep);
        var nextBlocks = DefaultBlockStructureForHours(weeklyHours);
        var shouldResetFixedTime = item.Sections.Any(sec => sec.IsFixed || sec.FixedSlots.Count > 0);

        foreach (var sec in item.Sections)
        {
            sec.HoursPerWeek = weeklyHours;
            sec.BlockStructure = new List<int>(nextBlocks);
        }

        if (hoursChanged || shouldResetFixedTime || !previousBlocks.SequenceEqual(nextBlocks))
        {
            ResetFixedTime(item);
            return true;
        }

        return false;
    }

    public bool HandleCourseBlockStructureChanged(CourseGroupItem item)
    {
        if (item.Sections.Count == 0) return false;
        var rep = item.Sections[0];
        if (!IsValidBlockStructure(rep.BlockStructure, rep.HoursPerWeek))
            rep.BlockStructure = DefaultBlockStructureForHours(rep.HoursPerWeek);

        ResetFixedTime(item);
        return true;
    }

    private static List<int> ParseBlockStructure(string text) =>
        text.Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.Parse(part.Trim()))
            .ToList();

    private static void ResetFixedTime(CourseGroupItem item)
    {
        foreach (var sec in item.Sections)
        {
            sec.IsFixed = false;
            sec.FixedSlots.Clear();
        }
    }

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
            .Where(s => s.IsFixed && s.FixedSlots.Count > 0)
            .Select(s => $"{SectionLetter(s.Section)}분반 {FormatSlotRuns(s.FixedSlots)}")
            .ToList();
        return labels.Count == 0 ? "" : string.Join(" / ", labels);
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
        if (slots.Count == 0) return "없음";
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
        var scheduleSnapshot = _workspace.SchedulingSnapshot();
        var unsavedEditLabels = GetUnsavedEditLabels();
        var inputErrors = TimetableDiagnostics.GetInputErrors(
            scheduleSnapshot.Courses,
            scheduleSnapshot.Professors,
            scheduleSnapshot.Rooms,
            scheduleSnapshot.CrossGroups,
            hasUnsavedEdits: unsavedEditLabels.Count > 0,
            unsavedEditSummary: string.Join(", ", unsavedEditLabels));
        if (inputErrors.Count > 0)
        {
            StatusMessage = FormatDiagnostics("입력 오류", inputErrors);
            IsSolveComplete = false;
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;

        IsSolving = true;
        IsSolveComplete = false;
        StatusMessage = "솔버 시작";
        ProgressCurrent = 0;
        ProgressTotal = TotalSolutions;
        UniqueSolutionsFound = 0;

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
                TimeLimitSec = Math.Max(1, TimeLimitSec),
                PerSolveTimeSec = 0,
                ConsiderRetakeStudents = ConsiderRetakeStudents,
                UseSc01 = UseSc01,
                UseSc02 = UseSc02,
                UseSc03 = UseSc03,
            };
            var result = await _solver.SolveAsync(_workspace, options, progress, cts.Token);
            var ranked = _solver.Rank(_workspace, result, topM: 10);
            RankedResults.Clear();
            foreach (var r in ranked) RankedResults.Add(r);
            StatusMessage = result.Status switch
            {
                "CANCELLED" => "취소됨",
                "INFEASIBLE" => InfeasibleMessage(),
                "UNKNOWN" when ranked.Count == 0 => UnknownMessageWithId(),
                _ when ranked.Count == 0 => NoSolutionMessageWithId(result.Status),
                _ => $"완료: {result.Status}, {result.Solutions.Count}개 해, 미리보기 {ranked.Count}개",
            };
            IsSolveComplete = result.Status != "CANCELLED" && ranked.Count > 0;
            if (IsSolveComplete)
                SolveCompleted?.Invoke(this, ranked);
        }
        catch (Exception ex) when (IsCancellationException(ex))
        {
            StatusMessage = "취소됨";
            IsSolveComplete = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"오류: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
            {
                _cts.Dispose();
                _cts = null;
            }
            IsSolving = false;
        }
    }

    private bool CanSolve() => !IsSolving && _workspace.Courses.Count > 0;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
            CancelCommand.NotifyCanExecuteChanged();
        }
        catch (ObjectDisposedException)
        {
            StatusMessage = "취소됨";
        }
    }

    private bool CanCancel() => IsSolving && _cts is { IsCancellationRequested: false };

    private static bool IsCancellationException(Exception exception)
    {
        if (exception is OperationCanceledException)
            return true;

        if (exception is AggregateException aggregate)
            return aggregate.Flatten().InnerExceptions.All(IsCancellationException);

        return false;
    }

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
            .Replace("opt=", "최적값=")
            .Replace("bound=", "허용상한=");
        return $"{message}";
    }

    private string InfeasibleMessage()
    {
        var scheduleSnapshot = _workspace.SchedulingSnapshot();
        var diagnostics = TimetableDiagnostics.GetGenerationErrors(
            scheduleSnapshot.Courses,
            scheduleSnapshot.Professors,
            scheduleSnapshot.Rooms,
            scheduleSnapshot.CrossGroups,
            scheduleSnapshot.RetakeScenarios,
            ConsiderRetakeStudents);
        if (diagnostics.Count > 0)
            return FormatDiagnostics("INFEASIBLE", diagnostics);

        var reasons = new List<string>();
        var professorsById = scheduleSnapshot.Professors.ToDictionary(professor => professor.Id);

        foreach (var course in scheduleSnapshot.Courses)
        {
            if (course.IsFixed && course.FixedSlots.Count == 0)
                reasons.Add($"fixed time conflict: {course.Name} 고정 시간이 비어 있습니다");

            if (course.IsFixed && course.FixedSlots.Any(slot => slot.Period == 5))
                reasons.Add($"fixed time conflict: {course.Name} 이 점심 시간에 고정되어 있습니다");

            if (course.BlockStructure.Count > 0 && course.BlockStructure.Sum() != course.HoursPerWeek)
                reasons.Add($"insufficient available time slots: {course.Name} 블록 합이 시수와 다릅니다");

            var availableRoomCount = scheduleSnapshot.Rooms.Count(room =>
                !course.UnavailableRooms.Contains(room.Id) &&
                (course.FixedRooms.Count == 0 || course.FixedRooms.Contains(room.Id)));
            if (scheduleSnapshot.Rooms.Count > 0 && availableRoomCount == 0)
                reasons.Add($"room capacity/type conflict: {course.Name} 에 사용 가능한 강의실이 없습니다");

            if (professorsById.TryGetValue(course.ProfessorId, out var professor))
            {
                if (course.IsFixed && course.FixedSlots.Any(slot => professor.UnavailableSlots.Any(p => p.Day == slot.Day && p.Period == slot.Period)))
                    reasons.Add($"professor unavailable time conflict: {course.Name} / {professor.Name}");

                var availableProfessorRooms = scheduleSnapshot.Rooms.Count(room =>
                    !course.UnavailableRooms.Contains(room.Id) &&
                    !professor.UnavailableRooms.Contains(room.Id) &&
                    (course.FixedRooms.Count == 0 || course.FixedRooms.Contains(room.Id)) &&
                    (professor.AllowedRooms.Count == 0 || professor.AllowedRooms.Contains(room.Id) || course.FixedRooms.Contains(room.Id)));
                if (scheduleSnapshot.Rooms.Count > 0 && availableProfessorRooms == 0)
                    reasons.Add($"room capacity/type conflict: {course.Name} / {professor.Name} 조건을 만족하는 강의실이 없습니다");

                if (!course.IsFixed)
                {
                    var availableSlots = Constants.Periods
                        .Where(period => period != 5)
                        .SelectMany(period => Enumerable.Range(0, 5).Select(day => new TimeSlot(day, period)))
                        .Count(slot => professor.UnavailableSlots.All(p => p.Day != slot.Day || p.Period != slot.Period));
                    if (availableSlots < Math.Max(course.HoursPerWeek, course.BlockStructure.Sum()))
                        reasons.Add($"insufficient available time slots: {course.Name} 과목에 대해 {professor.Name} 교수의 가용 시간이 부족합니다");
                }
            }
        }

        foreach (var prof in scheduleSnapshot.Professors)
            if (scheduleSnapshot.Rooms.Count > 0 && prof.UnavailableRooms.Count >= scheduleSnapshot.Rooms.Count)
                reasons.Add($"room capacity/type conflict: {prof.Name} 교수의 사용 가능 강의실이 없습니다");
        foreach (var course in scheduleSnapshot.Courses)
            if (scheduleSnapshot.Rooms.Count > 0 && course.UnavailableRooms.Count >= scheduleSnapshot.Rooms.Count)
                reasons.Add($"room capacity/type conflict: {course.Name} 과목의 사용 가능 강의실이 없습니다");

        foreach (var cross in scheduleSnapshot.CrossGroups)
        {
            if (cross.BaseIds.Count != 2)
            {
                reasons.Add($"cross constraint conflict: {cross.Id} 는 과목 2개만 포함해야 합니다");
                continue;
            }

            var groupedCourses = cross.BaseIds
                .Select(id => new { Id = id, Courses = scheduleSnapshot.Courses.Where(course => DomainHelpers.BaseId(course.Id) == id).ToList() })
                .ToList();
            if (groupedCourses.Any(group => group.Courses.Count == 0))
            {
                reasons.Add($"cross constraint conflict: {cross.Id} 대상 과목을 찾을 수 없습니다");
                continue;
            }

            if (groupedCourses.Select(group => group.Courses.Count).Distinct().Count() > 1)
                reasons.Add($"cross constraint conflict: {cross.Id} 분반 수가 다릅니다");

            if (groupedCourses.Select(group => group.Courses[0].HoursPerWeek).Distinct().Count() > 1)
                reasons.Add($"cross constraint conflict: {cross.Id} 총 시수가 다릅니다");
        }

        return reasons.Count == 0
            ? InfeasibleFallbackMessage()
            : $"INFEASIBLE: {string.Join(" / ", reasons.Distinct())}";
    }

    private static string InfeasibleFallbackMessage() =>
        "INFEASIBLE: GE-025: 정확한 충돌 원인은 자동으로 특정하지 못했습니다. 수정 후보: 시간 고정, 교수 불가시간, 불가 강의실, 고정 강의실, Cross, 재수강 조건을 하나씩 줄인 뒤 다시 생성해 보세요.";

    private IReadOnlyList<string> GetUnsavedEditLabels() =>
        CourseGroups.Where(item => item.IsEditing)
            .Select(item => $"교과목 관리 > {EditItemLabel(item.HeaderName, item.BaseId)}")
            .Concat(ProfessorItems.Where(item => item.IsEditing)
                .Select(item => $"교수 관리 > {EditItemLabel(item.HeaderName, item.HeaderId)}"))
            .Concat(RoomItems.Where(item => item.IsEditing)
                .Select(item => $"강의실 관리 > {EditItemLabel(item.HeaderName, item.HeaderId)}"))
            .ToList();

    private static string EditItemLabel(string name, string id) =>
        string.IsNullOrWhiteSpace(name) ? id : $"{name} ({id})";

    private static string CourseInputLocation(Course course) =>
        $"교과목 관리 > {EditItemLabel(course.Name, course.Id)}";

    private static string ProfessorInputLocation(Professor professor) =>
        $"교수 관리 > {EditItemLabel(professor.Name, professor.Id)}";

    private static string RoomInputLocation(Room room) =>
        $"강의실 관리 > {EditItemLabel(room.Name, room.Id)}";

    private static string FormatDiagnostics(string title, IReadOnlyList<TimetableDiagnostic> diagnostics)
    {
        var ordered = OrderDiagnosticsForStatus(diagnostics);
        var shown = ordered.Take(5).Select(diagnostic => diagnostic.ToString()).ToList();
        var suffix = diagnostics.Count > shown.Count
            ? $" / 외 {diagnostics.Count - shown.Count}건"
            : "";
        return $"{title}: {string.Join(" / ", shown)}{suffix}";
    }

    private static IReadOnlyList<TimetableDiagnostic> OrderDiagnosticsForStatus(IReadOnlyList<TimetableDiagnostic> diagnostics) =>
        diagnostics
            .Select((diagnostic, index) => new { Diagnostic = diagnostic, Index = index })
            .OrderBy(item => item.Diagnostic.Id == "IE-004" ? 0 : 1)
            .ThenBy(item => item.Index)
            .Select(item => item.Diagnostic)
            .ToList();

    private static string UnknownMessageWithId() =>
        "GE-022: 전체 생성 제한 시간 안에 해를 찾거나 불가능함을 확정하지 못했습니다. 제한 시간을 늘려 다시 시도해 보세요.";

    private static string NoSolutionMessageWithId(string status) =>
        status == "MODEL_INVALID"
            ? "GE-023: 모델이 유효하지 않습니다. 입력 데이터 참조와 조건을 점검해 주세요."
            : $"GE-024: 해를 찾지 못했습니다({status}). 불가 시간, 불가 강의실, 시간 고정, Cross 설정을 줄이거나 제한 시간을 늘려보세요.";

    private string UnknownMessage() =>
        "전체 생성 제한 시간 안에 해를 찾거나 불가능함을 확정하지 못했습니다. 제한 시간을 늘려 다시 시도해 보세요.";

    private string NoSolutionMessage(string status) =>
        $"해를 찾지 못했습니다({status}). 불가 시간, 불가 강의실, 시간 고정, Cross 설정을 줄이거나 제한 시간을 늘려보세요.";
}
