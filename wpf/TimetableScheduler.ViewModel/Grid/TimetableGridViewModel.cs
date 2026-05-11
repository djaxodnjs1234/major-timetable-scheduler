using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Grid;

public sealed partial class TimetableGridViewModel : ObservableObject
{
    public IReadOnlyList<int> Days { get; } = Enumerable.Range(0, Constants.Days).ToList();
    public IReadOnlyList<int> Periods { get; } = Constants.Periods;

    public ObservableCollection<SlotCellViewModel> Cells { get; } = new();

    public TimetableGridViewModel()
    {
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in Constants.Periods)
                Cells.Add(new SlotCellViewModel(d, p));
    }

    public SlotCellViewModel CellAt(int day, int period)
    {
        var idx = day * Constants.Periods.Count + (period - 1);
        return Cells[idx];
    }

    public void Render(
        IReadOnlyList<SolutionAssignment> assignment,
        IReadOnlyList<Course> courses,
        Func<Course, string, bool>? accept = null)
    {
        foreach (var c in Cells) c.Clear();

        var courseMap = courses.ToDictionary(c => c.Id);
        var runs = TimetableRuns.ComputeRuns(assignment);

        // group by (cid, day, period) → set of rooms (for multi-room courses)
        var roomsBySlot = new Dictionary<(string Cid, int Day, int Period), HashSet<string>>();
        foreach (var a in assignment)
        {
            if (!courseMap.TryGetValue(a.CourseId, out var c)) continue;
            if (accept != null && !accept(c, a.RoomId)) continue;
            var key = (a.CourseId, a.Day, a.Period);
            if (!roomsBySlot.TryGetValue(key, out var set))
                roomsBySlot[key] = set = new HashSet<string>();
            set.Add(a.RoomId);
        }

        // For each unique (cid, day, period) entry, place CellAssignment at the START of the run only.
        // Skip cells that are "inside" the run.
        foreach (var ((cid, d, p), rooms) in roomsBySlot)
        {
            if (runs.Inside.Contains((cid, d, p))) continue;
            var c = courseMap[cid];
            int rs = runs.RunLen.TryGetValue((cid, d, p), out int n) ? n : 1;
            var cell = CellAt(d, p);
            cell.Add(CellAssignment.FromCourse(c, rooms, rs));
        }
    }
}
