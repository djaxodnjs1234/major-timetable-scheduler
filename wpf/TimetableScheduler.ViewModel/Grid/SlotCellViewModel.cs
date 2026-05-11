using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TimetableScheduler.ViewModel.Grid;

public sealed partial class SlotCellViewModel : ObservableObject
{
    public int Day { get; }
    public int Period { get; }
    public bool IsLunch => Period == 5;

    public ObservableCollection<CellAssignment> Items { get; } = new();

    [ObservableProperty]
    private bool isOccupied;

    public SlotCellViewModel(int day, int period)
    {
        Day = day;
        Period = period;
    }

    public void Clear()
    {
        Items.Clear();
        IsOccupied = false;
    }

    public void Add(CellAssignment a)
    {
        Items.Add(a);
        IsOccupied = true;
    }
}
