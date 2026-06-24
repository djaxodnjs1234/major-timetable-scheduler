namespace TimetableScheduler.Domain;

public static class SchedulePeriods
{
    public const int LunchPeriod = 5;
    public const int FirstNightPeriod = 10;
    public const int LastPeriod = 13;

    public static IReadOnlyList<int> All { get; } =
        Array.AsReadOnly(Enumerable.Range(1, LastPeriod).ToArray());

    public static IReadOnlyList<int> Daytime { get; } =
        Array.AsReadOnly(All.Where(period => period != LunchPeriod && period < FirstNightPeriod).ToArray());

    public static IReadOnlyList<int> Night { get; } =
        Array.AsReadOnly(Enumerable.Range(FirstNightPeriod, LastPeriod - FirstNightPeriod + 1).ToArray());

    public static IReadOnlyList<int> Instructional { get; } =
        Array.AsReadOnly(All.Where(period => period != LunchPeriod).ToArray());

    public static bool IsNight(int period) => period >= FirstNightPeriod && period <= LastPeriod;

    public static string TimeRange(int period) =>
        $"{8 + period:00}:00~{9 + period:00}:00";
}
