using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

public static class Constants
{
    public const int FirstNightPeriod = SchedulePeriods.FirstNightPeriod;
    public const int Days = 5;

    public static readonly IReadOnlyList<int> Periods =
        SchedulePeriods.All;

    public static readonly IReadOnlyList<int> NightPeriods =
        SchedulePeriods.Night;
}
