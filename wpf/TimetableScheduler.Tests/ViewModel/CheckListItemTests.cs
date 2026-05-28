using TimetableScheduler.ViewModel.Editors;

namespace TimetableScheduler.Tests.ViewModel;

public class CheckListItemTests
{
    [Fact]
    public void Bind_PreCheckedItems_ReflectsState()
    {
        var source = new[] { ("R1", "A"), ("R2", "B"), ("R3", "C") };
        var selected = new List<string> { "R1", "R3" };
        var items = CheckListBinder.Bind(source, x => x.Item1, x => x.Item2, selected);

        Assert.True(items[0].IsChecked);
        Assert.False(items[1].IsChecked);
        Assert.True(items[2].IsChecked);
    }

    [Fact]
    public void Toggle_UpdatesUnderlyingList()
    {
        var source = new[] { ("R1", "A"), ("R2", "B") };
        var selected = new List<string>();
        var items = CheckListBinder.Bind(source, x => x.Item1, x => x.Item2, selected);

        items[0].IsChecked = true;
        Assert.Single(selected);
        Assert.Contains("R1", selected);

        items[0].IsChecked = false;
        Assert.Empty(selected);
    }

    [Fact]
    public void Toggle_AvoidsDuplicates()
    {
        var source = new[] { ("R1", "A") };
        var selected = new List<string> { "R1" };
        var items = CheckListBinder.Bind(source, x => x.Item1, x => x.Item2, selected);

        // already true; setting true again should not add duplicate
        items[0].IsChecked = false;
        items[0].IsChecked = true;
        Assert.Single(selected);
    }
}
