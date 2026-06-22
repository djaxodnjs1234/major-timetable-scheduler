using System.Collections.ObjectModel;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel;

public sealed class WorkspaceService
{
    // Null for a session workspace: CRUD stays in memory, never touches a DB.
    private readonly SqliteRepository? _repo;

    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<Professor> Professors { get; } = new();
    public ObservableCollection<Room> Rooms { get; } = new();
    public ObservableCollection<CrossGroup> CrossGroups { get; } = new();
    public ObservableCollection<RetakeScenario> RetakeScenarios { get; } = new();
    public ObservableCollection<SavedTimetableRecord> SavedTimetables { get; } = new();
    private readonly HashSet<string> _importedRoomIds = new(StringComparer.Ordinal);
    public IReadOnlySet<string> ImportedRoomIds => _importedRoomIds;

    /// <summary>True for a session workspace (snapshot-backed, no DB persistence).</summary>
    public bool IsSession => _repo == null;

    public event EventHandler? Changed;

    public WorkspaceService(SqliteRepository repo)
    {
        _repo = repo;
        _repo.EnsureCreated();
        Reload();
    }

    private WorkspaceService(AppData snapshot)
    {
        _repo = null;
        LoadFrom(snapshot);
    }

    /// <summary>
    /// Create an in-memory session workspace seeded from a saved timetable's snapshot.
    /// CRUD on it never persists to any DB.
    /// </summary>
    public static WorkspaceService CreateSession(AppData snapshot) => new(snapshot);

    private void LoadFrom(AppData data)
    {
        Courses.Clear();
        foreach (var c in data.Courses) Courses.Add(c);
        Professors.Clear();
        foreach (var p in data.Professors) Professors.Add(p);
        Rooms.Clear();
        foreach (var r in data.Rooms) Rooms.Add(r);
        _importedRoomIds.Clear();
        CrossGroups.Clear();
        foreach (var g in data.CrossGroups) CrossGroups.Add(g);
        RetakeScenarios.Clear();
        foreach (var r in data.RetakeScenarios) RetakeScenarios.Add(r);
        RaiseChanged();
    }

    public void Reload()
    {
        if (_repo == null) return;
        var data = _repo.LoadAll();
        Courses.Clear();
        foreach (var c in data.Courses) Courses.Add(c);
        Professors.Clear();
        foreach (var p in data.Professors) Professors.Add(p);
        Rooms.Clear();
        foreach (var r in data.Rooms) Rooms.Add(r);
        _importedRoomIds.Clear();
        CrossGroups.Clear();
        foreach (var g in data.CrossGroups) CrossGroups.Add(g);
        RetakeScenarios.Clear();
        foreach (var r in data.RetakeScenarios) RetakeScenarios.Add(r);
        SavedTimetables.Clear();
        foreach (var t in _repo.LoadSavedTimetables()) SavedTimetables.Add(t);
        RaiseChanged();
    }

    // SaveTimetable / DeleteSavedTimetable / Export/ImportDatabase are global-only:
    // a session workspace (_repo == null) is never expected to call them.
    public SavedTimetableRecord SaveTimetable(
        string name,
        IReadOnlyList<SolutionAssignment> assignments,
        IReadOnlyList<SavedManualCrossLinkRow>? manualCrossLinks = null,
        AppData? snapshot = null,
        string? id = null)
    {
        var rows = assignments
            .Select(a => new TimetableAssignmentRow(a.CourseId, a.Day, a.Period, a.RoomId, a.AssignmentId))
            .ToList();
        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshot ?? Snapshot());
        var recordId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
        var record = new SavedTimetableRecord(
            recordId,
            name,
            DateTime.Now,
            rows,
            manualCrossLinks?.ToList() ?? new List<SavedManualCrossLinkRow>(),
            snapshotJson);
        _repo!.UpsertSavedTimetable(record);
        var existing = SavedTimetables.FirstOrDefault(t => t.Id == record.Id);
        if (existing == null)
        {
            SavedTimetables.Insert(0, record);
        }
        else
        {
            var index = SavedTimetables.IndexOf(existing);
            SavedTimetables[index] = record;
        }
        return record;
    }

    public void DeleteSavedTimetable(string id)
    {
        _repo!.DeleteSavedTimetable(id);
        var item = SavedTimetables.FirstOrDefault(t => t.Id == id);
        if (item != null) SavedTimetables.Remove(item);
    }

    public void ExportDatabase(string destPath) => _repo!.ExportTo(destPath);

    public void ImportDatabase(string sourcePath)
    {
        _repo!.ReplaceWith(sourcePath);
        Reload();
    }

    public void ImportFromXlsx(string path)
    {
        var loaded = XlsxLoader.Load(path);
        Courses.Clear();
        foreach (var c in loaded.Courses) Courses.Add(c);
        Professors.Clear();
        foreach (var p in loaded.Professors) Professors.Add(p);
        Rooms.Clear();
        foreach (var r in loaded.Rooms) Rooms.Add(r);
        _importedRoomIds.Clear();
        foreach (var room in loaded.Rooms)
            room.IsImportedFromExcel = true;
        foreach (var roomId in loaded.Rooms.Select(r => r.Id).Where(id => !string.IsNullOrWhiteSpace(id)))
            _importedRoomIds.Add(roomId);
        CrossGroups.Clear();
        RetakeScenarios.Clear();
        Persist();
    }

    public void AddCourse(Course c) { Courses.Add(c); Persist(); }
    public void DeleteCourse(Course course)
    {
        var c = Courses.FirstOrDefault(x => x.Id == course.Id && x.Section == course.Section);
        if (c == null) return;
        if (IsCourseInUse(c))
            throw new InvalidOperationException("이 교과목은 관련 조건에서 사용 중이므로 삭제할 수 없습니다.");
        Courses.Remove(c);
        Persist();
    }
    public void UpdateCourse(Course c)
    {
        var idx = IndexOfCourse(c.Id, c.Section);
        if (idx < 0) return;
        Courses[idx] = c;
        Persist();
    }

    public void AddProfessor(Professor p) { Professors.Add(p); Persist(); }
    public void DeleteProfessor(string id)
    {
        var p = Professors.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        if (IsProfessorInUse(id))
            throw new InvalidOperationException("이 교수는 교과목 또는 설정에서 사용 중이므로 삭제할 수 없습니다.");
        Professors.Remove(p);
        Persist();
    }
    public void UpdateProfessor(Professor p)
    {
        var idx = IndexOfProf(p.Id);
        if (idx < 0) return;
        Professors[idx] = p;
        Persist();
    }

    public void AddRoom(Room r) { Rooms.Add(r); Persist(); }
    public void DeleteRoom(string id)
    {
        var r = Rooms.FirstOrDefault(x => x.Id == id);
        if (r == null) return;
        if (IsRoomInUse(id))
            throw new InvalidOperationException("이 강의실은 교과목 또는 교수 설정에서 사용 중이므로 삭제할 수 없습니다.");
        Rooms.Remove(r);
        Persist();
    }
    public void UpdateRoom(Room r)
    {
        var idx = Rooms.IndexOf(Rooms.FirstOrDefault(x => x.Id == r.Id) ?? r);
        if (idx < 0) return;
        Rooms[idx] = r;
        Persist();
    }

    public void AddCrossGroup(CrossGroup g) { CrossGroups.Add(g); Persist(); }
    public void DeleteCrossGroup(string id)
    {
        var g = CrossGroups.FirstOrDefault(x => x.Id == id);
        if (g == null) return;
        CrossGroups.Remove(g);
        Persist();
    }

    public void AddRetake(RetakeScenario r) { RetakeScenarios.Add(r); Persist(); }
    public void DeleteRetake(int grade, string baseId)
    {
        var r = RetakeScenarios.FirstOrDefault(x => x.CurrentGrade == grade && x.RetakeBaseId == baseId);
        if (r == null) return;
        RetakeScenarios.Remove(r);
        Persist();
    }

    public IReadOnlyList<Course> ExpandedCourses => Courses.ToList();

    public AppData Snapshot() => new(
        Courses.ToList(),
        Professors.ToList(), Rooms.ToList(),
        CrossGroups.ToList(), RetakeScenarios.ToList());

    public AppData SchedulingSnapshot() => new(
        NormalizeCourseGroupsForScheduling(Courses),
        Professors.ToList(),
        Rooms.ToList(),
        CrossGroups.ToList(),
        RetakeScenarios.ToList());

    private static List<Course> NormalizeCourseGroupsForScheduling(IEnumerable<Course> courses)
    {
        var normalized = courses.Select(CloneCourse).ToList();
        foreach (var group in normalized
            .GroupBy(course => DomainHelpers.BaseId(course.Id))
            .Where(group => group.Count() > 1))
        {
            var sections = group.OrderBy(course => course.Section).ToList();
            var rep = sections[0];
            foreach (var section in sections.Skip(1))
            {
                section.Name = rep.Name;
                section.Grade = rep.Grade;
                section.HoursPerWeek = rep.HoursPerWeek;
                section.CourseType = rep.CourseType;
                section.Department = rep.Department;
                section.IsFixed = rep.IsFixed;
                if (!section.IsFixed)
                    section.FixedSlots.Clear();
                section.FixedRooms = new List<string>(rep.FixedRooms);
                section.UnavailableRooms = new List<string>(rep.UnavailableRooms);
                section.BlockStructure = new List<int>(rep.BlockStructure);
                section.CoteachProfs = new List<string>(rep.CoteachProfs);
            }
        }

        return normalized;
    }

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

    private bool IsProfessorInUse(string id)
    {
        if (Courses.Any(c =>
            string.Equals(c.ProfessorId, id, StringComparison.Ordinal)
            || c.CoteachProfs.Contains(id, StringComparer.Ordinal)))
            return true;

        var professor = Professors.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        return professor != null
            && (professor.UnavailableSlots.Count > 0 || professor.UnavailableRooms.Count > 0);
    }

    private bool IsRoomInUse(string id) =>
        Courses.Any(c =>
            c.FixedRooms.Contains(id, StringComparer.Ordinal)
            || c.UnavailableRooms.Contains(id, StringComparer.Ordinal))
        || Professors.Any(p => p.UnavailableRooms.Contains(id, StringComparer.Ordinal));

    private bool IsCourseInUse(Course course)
    {
        var baseId = DomainHelpers.BaseId(course.Id);
        return CrossGroups.Any(g => g.BaseIds.Contains(baseId, StringComparer.Ordinal))
            || RetakeScenarios.Any(r => string.Equals(r.RetakeBaseId, baseId, StringComparison.Ordinal));
    }

    private int IndexOfCourse(string id, int section)
    {
        for (int i = 0; i < Courses.Count; i++)
            if (Courses[i].Id == id && Courses[i].Section == section) return i;
        return -1;
    }

    private int IndexOfProf(string id)
    {
        for (int i = 0; i < Professors.Count; i++)
            if (Professors[i].Id == id) return i;
        return -1;
    }

    private void Persist()
    {
        _repo?.SaveAll(Snapshot());
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
