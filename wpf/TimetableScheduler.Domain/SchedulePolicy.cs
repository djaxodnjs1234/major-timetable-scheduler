namespace TimetableScheduler.Domain;

public enum LunchPolicyMode
{
    None,
    BanPeriod4,
    BanPeriod5,
    BanOneOfPeriods4And5,
}

public sealed record SchedulePolicy
{
    public LunchPolicyMode LunchMode { get; init; } = LunchPolicyMode.BanPeriod5;

    public static SchedulePolicy Default { get; } = new();
}

public static class SchedulePolicyRules
{
    public const int FirstLunchCandidate = 4;
    public const int SecondLunchCandidate = 5;

    public static IReadOnlySet<int> HardBlockedPeriods(SchedulePolicy policy) =>
        policy.LunchMode switch
        {
            LunchPolicyMode.None => new HashSet<int>(),
            LunchPolicyMode.BanPeriod4 => new HashSet<int> { FirstLunchCandidate },
            LunchPolicyMode.BanPeriod5 => new HashSet<int> { SecondLunchCandidate },
            LunchPolicyMode.BanOneOfPeriods4And5 => new HashSet<int>(),
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

    public static IReadOnlyList<int> CandidateInstructionalPeriods(SchedulePolicy policy)
    {
        var blocked = HardBlockedPeriods(policy);
        return SchedulePeriods.All.Where(period => !blocked.Contains(period)).ToArray();
    }

    public static IReadOnlyList<int> CandidateDaytimePeriods(SchedulePolicy policy)
    {
        var blocked = HardBlockedPeriods(policy);
        return SchedulePeriods.Daytime.Where(period => !blocked.Contains(period)).ToArray();
    }

    public static bool IsLunchCandidate(int period) =>
        period is FirstLunchCandidate or SecondLunchCandidate;

    public static bool UsesFlexibleLunch(SchedulePolicy policy) =>
        policy.LunchMode == LunchPolicyMode.BanOneOfPeriods4And5;

    public static bool IsStaticallyBlocked(SchedulePolicy policy, int period) =>
        HardBlockedPeriods(policy).Contains(period);

    public static int? StaticLunchPeriod(SchedulePolicy policy) =>
        policy.LunchMode switch
        {
            LunchPolicyMode.None => null,
            LunchPolicyMode.BanPeriod4 => FirstLunchCandidate,
            LunchPolicyMode.BanPeriod5 => SecondLunchCandidate,
            LunchPolicyMode.BanOneOfPeriods4And5 => null,
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

    public static IReadOnlyDictionary<int, int> StaticLunchPeriodsByDay(
        SchedulePolicy policy,
        int days)
    {
        var lunchPeriod = StaticLunchPeriod(policy);
        return lunchPeriod.HasValue
            ? Enumerable.Range(0, days).ToDictionary(day => day, _ => lunchPeriod.Value)
            : new Dictionary<int, int>();
    }

    public static bool IsLunch(
        SchedulePolicy policy,
        IReadOnlyDictionary<int, int>? lunchPeriodsByDay,
        int day,
        int period)
    {
        if (policy.LunchMode == LunchPolicyMode.None)
            return false;

        var staticLunch = StaticLunchPeriod(policy);
        if (staticLunch.HasValue)
            return period == staticLunch.Value;

        return lunchPeriodsByDay != null
            && lunchPeriodsByDay.TryGetValue(day, out var lunchPeriod)
            && lunchPeriod == period;
    }

    public static IReadOnlyList<int> DeriveTwoHourStarts(IEnumerable<int> periods)
    {
        var ordered = periods.Distinct().OrderBy(period => period).ToList();
        var starts = new List<int>();
        var run = new List<int>();

        void AddRunStarts()
        {
            for (var index = 0; index + 1 < run.Count; index += 2)
                starts.Add(run[index]);
            run.Clear();
        }

        foreach (var period in ordered)
        {
            var startsNightBand = period == SchedulePeriods.FirstNightPeriod;
            if (run.Count > 0
                && (period != run[^1] + 1 || startsNightBand))
            {
                AddRunStarts();
            }
            run.Add(period);
        }
        AddRunStarts();
        return starts;
    }

    public static IReadOnlyList<int> PossibleBlockStarts(
        SchedulePolicy policy,
        IReadOnlyList<int> academicPeriods,
        int blockLength)
    {
        if (blockLength <= 0)
            return Array.Empty<int>();

        if (!UsesFlexibleLunch(policy))
            return StartsForAllowedPeriods(academicPeriods, blockLength);

        var starts = new HashSet<int>();
        foreach (var lunchPeriod in new[] { FirstLunchCandidate, SecondLunchCandidate })
        {
            var allowed = academicPeriods.Where(period => period != lunchPeriod).ToArray();
            foreach (var start in StartsForAllowedPeriods(allowed, blockLength))
                starts.Add(start);
        }
        return starts.OrderBy(start => start).ToArray();
    }

    public static bool BlockUsesBothLunchCandidates(int startPeriod, int blockLength)
    {
        var periods = Enumerable.Range(startPeriod, blockLength).ToHashSet();
        return periods.Contains(FirstLunchCandidate)
            && periods.Contains(SecondLunchCandidate);
    }

    private static IReadOnlyList<int> StartsForAllowedPeriods(
        IReadOnlyList<int> allowedPeriods,
        int blockLength)
    {
        var allowed = allowedPeriods.ToHashSet();
        var starts = allowedPeriods
            .Where(start => Enumerable.Range(start, blockLength).All(allowed.Contains));
        if (blockLength == 2)
        {
            var twoHourStarts = DeriveTwoHourStarts(allowedPeriods).ToHashSet();
            starts = starts.Where(twoHourStarts.Contains);
        }
        return starts.Distinct().OrderBy(start => start).ToArray();
    }
}
