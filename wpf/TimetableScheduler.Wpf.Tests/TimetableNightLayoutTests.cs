using TimetableScheduler.Wpf.Controls;

namespace TimetableScheduler.Wpf.Tests;

public class TimetableNightLayoutTests
{
    [Fact]
    public void StandardGrid_InsertsNightSeparatorBeforePeriod10()
    {
        Assert.Equal(8, TimetableGridControl.BodyRowForPeriod(9));
        Assert.Equal(9, TimetableGridControl.NightSeparatorRow);
        Assert.Equal(10, TimetableGridControl.BodyRowForPeriod(10));
    }

    [Fact]
    public void UnifiedGrid_InsertsNightSeparatorBeforePeriod10()
    {
        Assert.Equal(10, UnifiedTimetableControl.GridRowForPeriod(9));
        Assert.Equal(11, UnifiedTimetableControl.NightSeparatorRow);
        Assert.Equal(12, UnifiedTimetableControl.GridRowForPeriod(10));
    }
}
