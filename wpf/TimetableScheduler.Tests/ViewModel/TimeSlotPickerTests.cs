using TimetableScheduler.Domain;
using TimetableScheduler.ViewModel.Editors;
using TimetableScheduler.ViewModel.Pages;

namespace TimetableScheduler.Tests.ViewModel;

public class TimeSlotPickerTests
{
    [Fact]
    public void Constructor_PreloadsSelected()
    {
        var target = new List<TimeSlot> { new(0, 3), new(2, 7) };
        var vm = new TimeSlotPickerViewModel(target);

        Assert.True(vm.CellAt(0, 3).IsSelected);
        Assert.True(vm.CellAt(2, 7).IsSelected);
        Assert.False(vm.CellAt(0, 1).IsSelected);
    }

    [Fact]
    public void Toggle_AddsAndRemovesFromTarget()
    {
        var target = new List<TimeSlot>();
        var vm = new TimeSlotPickerViewModel(target);

        vm.Toggle(1, 3);
        Assert.Contains(new TimeSlot(1, 3), target);

        vm.Toggle(1, 3);
        Assert.Empty(target);
    }

    [Fact]
    public void Toggle_IgnoresLunchPeriod()
    {
        var target = new List<TimeSlot>();
        var vm = new TimeSlotPickerViewModel(target);

        vm.Toggle(0, 5);  // lunch
        Assert.Empty(target);
        Assert.False(vm.CellAt(0, 5).IsSelected);
    }

    [Fact]
    public void FixedSlotEditor_LenTwoBlocksUseTimeRangesAndAllowedStarts()
    {
        var item = new CourseGroupItem
        {
            BaseId = "X",
            Sections = new List<Course>
            {
                new() { Id = "X-01", HoursPerWeek = 3, BlockStructure = new List<int> { 2, 1 }, IsFixed = true },
            },
        };

        var vm = FixedSlotEditorViewModel.Build(item, isFixed: true);
        var firstBlock = vm.SectionEditors.Single().BlockEntries[0];

        Assert.Equal(new[] { 1, 3, 6, 8 }, firstBlock.PeriodOptions.Select(o => o.Period));
        Assert.Contains("09:00~11:00", firstBlock.PeriodOptions[0].Label);
    }
}
