namespace TimetableScheduler.Solver;

public static class Constants
{
    public const int LunchPeriod = 5;
    public const int Days = 5;

    public static readonly IReadOnlyList<int> Periods =
        Enumerable.Range(1, 9).ToList();

    public static readonly IReadOnlyList<int> ValidPeriods =
        Enumerable.Range(1, 9).Where(p => p != LunchPeriod).ToList();

    public static readonly IReadOnlyList<int> Len2StartPeriods =
        new[] { 1, 3, 6, 8 };
}
