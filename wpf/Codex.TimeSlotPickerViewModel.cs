using CommunityToolkit.Mvvm.ComponentModel;
using TimetableScheduler.Domain;
using TimetableScheduler.Solver;

namespace TimetableScheduler.ViewModel.Editors;

public sealed partial class TimeSlotPickerCell : ObservableObject
{
    public int Day { get; }
    public int Period { get; }
    public bool IsLunch => Period == Constants.LunchPeriod;

    [ObservableProperty]
    private bool isSelected;

    public TimeSlotPickerCell(int day, int period, bool initial)
    {
        Day = day;
        Period = period;
        isSelected = initial;
    }
}

public sealed class TimeSlotPickerViewModel
{
    public IReadOnlyList<TimeSlotPickerCell> Cells { get; }
    private readonly IList<TimeSlot> _target;

    public TimeSlotPickerViewModel(IList<TimeSlot> target, IReadOnlyList<int>? periods = null)
    {
        _target = target;
        var visiblePeriods = periods ?? Constants.Periods;
        if (periods != null)
        {
            var allowedPeriods = visiblePeriods.ToHashSet();
            for (var i = target.Count - 1; i >= 0; i--)
            {
                if (!allowedPeriods.Contains(target[i].Period))
                    target.RemoveAt(i);
            }
        }

        var cells = new List<TimeSlotPickerCell>();
        for (int d = 0; d < Constants.Days; d++)
            foreach (var p in visiblePeriods)
            {
                bool initial = target.Contains(new TimeSlot(d, p));
                cells.Add(new TimeSlotPickerCell(d, p, initial));
            }
        Cells = cells;
    }

    public TimeSlotPickerCell CellAt(int day, int period) =>
        Cells.First(c => c.Day == day && c.Period == period);

    public void Toggle(int day, int period)
    {
        var cell = CellAt(day, period);
        if (cell.IsLunch) return;
        var slot = new TimeSlot(day, period);
        if (cell.IsSelected)
        {
            _target.Remove(slot);
            cell.IsSelected = false;
        }
        else
        {
            if (!_target.Contains(slot)) _target.Add(slot);
            cell.IsSelected = true;
        }
    }
}
