using TimetableScheduler.Domain;
using TimetableScheduler.Solver;
using TimetableScheduler.ViewModel.Editors;

namespace TimetableScheduler.Tests.ViewModel;

public class CodexTimeSlotPickerDaytimeTests
{
    [Fact]
    public void Constructor_WithPeriodsBeforeNight_HidesNightAndPrunesHiddenTargetSlots()
    {
        var target = new List<TimeSlot>
        {
            new(0, 1),
            new(0, Constants.FirstNightPeriod),
        };
        var periodsBeforeNight = Constants.Periods
            .Where(period => period < Constants.FirstNightPeriod)
            .ToList();

        var vm = new TimeSlotPickerViewModel(target, periodsBeforeNight);

        Assert.True(vm.CellAt(0, 1).IsSelected);
        Assert.True(vm.CellAt(0, Constants.LunchPeriod).IsLunch);
        Assert.DoesNotContain(vm.Cells, cell => cell.Period >= Constants.FirstNightPeriod);
        Assert.DoesNotContain(new TimeSlot(0, Constants.FirstNightPeriod), target);
        Assert.Throws<InvalidOperationException>(() => vm.CellAt(0, Constants.FirstNightPeriod));
    }
}
