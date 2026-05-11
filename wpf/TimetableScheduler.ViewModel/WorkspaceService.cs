using System.Collections.ObjectModel;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.ViewModel;

public sealed class WorkspaceService
{
    private readonly SqliteRepository _repo;

    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<Professor> Professors { get; } = new();
    public ObservableCollection<Room> Rooms { get; } = new();
    public ObservableCollection<CrossGroup> CrossGroups { get; } = new();
    public ObservableCollection<RetakeScenario> RetakeScenarios { get; } = new();

    public event EventHandler? Changed;

    public WorkspaceService(SqliteRepository repo)
    {
        _repo = repo;
        _repo.EnsureCreated();
        Reload();
    }

    public void Reload()
    {
        var data = _repo.LoadAll();
        Courses.Clear();
        foreach (var c in data.Courses) Courses.Add(c);
        Professors.Clear();
        foreach (var p in data.Professors) Professors.Add(p);
        Rooms.Clear();
        foreach (var r in data.Rooms) Rooms.Add(r);
        CrossGroups.Clear();
        foreach (var g in data.CrossGroups) CrossGroups.Add(g);
        RetakeScenarios.Clear();
        foreach (var r in data.RetakeScenarios) RetakeScenarios.Add(r);
        RaiseChanged();
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
        CrossGroups.Clear();
        RetakeScenarios.Clear();
        Persist();
    }

    public void AddCourse(Course c) { Courses.Add(c); Persist(); }
    public void DeleteCourse(string id)
    {
        var c = Courses.FirstOrDefault(x => x.Id == id);
        if (c == null) return;
        Courses.Remove(c);
        Persist();
    }
    public void UpdateCourse(Course c)
    {
        var idx = IndexOfCourse(c.Id);
        if (idx < 0) return;
        Courses[idx] = c;
        Persist();
    }

    public void AddProfessor(Professor p) { Professors.Add(p); Persist(); }
    public void DeleteProfessor(string id)
    {
        var p = Professors.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
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

    public AppData Snapshot() => new(
        Courses.ToList(), Professors.ToList(), Rooms.ToList(),
        CrossGroups.ToList(), RetakeScenarios.ToList());

    private int IndexOfCourse(string id)
    {
        for (int i = 0; i < Courses.Count; i++)
            if (Courses[i].Id == id) return i;
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
        _repo.SaveAll(Snapshot());
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
