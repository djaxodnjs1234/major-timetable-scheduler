using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

public static class Constants
{
    public const int LunchPeriod = SchedulePeriods.LunchPeriod;
    public const int FirstNightPeriod = SchedulePeriods.FirstNightPeriod;
    public const int Days = 5;

    public static readonly IReadOnlyList<int> Periods =
        SchedulePeriods.All;

    public static readonly IReadOnlyList<int> ValidPeriods =
        SchedulePeriods.Instructional;

    public static readonly IReadOnlyList<int> DaytimePeriods =
        SchedulePeriods.Daytime;

    public static readonly IReadOnlyList<int> NightPeriods =
        SchedulePeriods.Night;

    public static readonly IReadOnlyList<int> Len2StartPeriods =
        new[] { 1, 3, 6, 8, 10, 12 };
}
