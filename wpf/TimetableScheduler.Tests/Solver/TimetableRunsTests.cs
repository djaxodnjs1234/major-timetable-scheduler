using TimetableScheduler.Solver;

namespace TimetableScheduler.Tests.Solver;

public class TimetableRunsTests
{
    [Fact]
    public void Empty_EmptyResult()
    {
        var info = TimetableRuns.ComputeRuns(Array.Empty<SolutionAssignment>());
        Assert.Empty(info.RunLen);
        Assert.Empty(info.Inside);
    }

    [Fact]
    public void SinglePeriod_RunLength1_NoInside()
    {
        var info = TimetableRuns.ComputeRuns(new[]
        {
            new SolutionAssignment("X", 0, 3, "R"),
        });
        Assert.Equal(1, info.RunLen[("X", 0, 3)]);
        Assert.Empty(info.Inside);
    }

    [Fact]
    public void TwoConsecutivePeriods_RunLength2_OneInside()
    {
        var info = TimetableRuns.ComputeRuns(new[]
        {
            new SolutionAssignment("X", 0, 1, "R"),
            new SolutionAssignment("X", 0, 2, "R"),
        });
        Assert.Equal(2, info.RunLen[("X", 0, 1)]);
        Assert.Contains(("X", 0, 2), info.Inside);
    }

    [Fact]
    public void ThreeConsecutivePeriods_RunLength3_TwoInside()
    {
        var info = TimetableRuns.ComputeRuns(new[]
        {
            new SolutionAssignment("X", 0, 6, "R"),
            new SolutionAssignment("X", 0, 7, "R"),
            new SolutionAssignment("X", 0, 8, "R"),
        });
        Assert.Equal(3, info.RunLen[("X", 0, 6)]);
        Assert.Contains(("X", 0, 7), info.Inside);
        Assert.Contains(("X", 0, 8), info.Inside);
    }

    [Fact]
    public void NonConsecutive_TwoRuns()
    {
        var info = TimetableRuns.ComputeRuns(new[]
        {
            new SolutionAssignment("X", 0, 1, "R"),
            new SolutionAssignment("X", 0, 3, "R"),
        });
        Assert.Equal(1, info.RunLen[("X", 0, 1)]);
        Assert.Equal(1, info.RunLen[("X", 0, 3)]);
        Assert.Empty(info.Inside);
    }

    [Fact]
    public void DifferentDays_TreatedSeparately()
    {
        var info = TimetableRuns.ComputeRuns(new[]
        {
            new SolutionAssignment("X", 0, 1, "R"),
            new SolutionAssignment("X", 0, 2, "R"),
            new SolutionAssignment("X", 1, 1, "R"),
        });
        Assert.Equal(2, info.RunLen[("X", 0, 1)]);
        Assert.Equal(1, info.RunLen[("X", 1, 1)]);
    }

    [Fact]
    public void MultiRoomSameSlot_TreatedAsSingleSlot()
    {
        // Same (cid, d, p) with two rooms — should still be one slot,
        // not double-counted.
        var info = TimetableRuns.ComputeRuns(new[]
        {
            new SolutionAssignment("X", 0, 1, "R1"),
            new SolutionAssignment("X", 0, 1, "R2"),
            new SolutionAssignment("X", 0, 2, "R1"),
            new SolutionAssignment("X", 0, 2, "R2"),
        });
        Assert.Equal(2, info.RunLen[("X", 0, 1)]);
    }
}
