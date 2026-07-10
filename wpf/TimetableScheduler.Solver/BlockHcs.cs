using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

using XDict = Dictionary<(string CourseId, int Day, int Period, string RoomId), BoolVar>;
using StartVarMap = Dictionary<(string CourseId, int BlockIdx), Dictionary<(int Day, int StartPeriod), BoolVar>>;
using DayVarMap = Dictionary<string, List<IntVar>>;
using LunchVarMap = Dictionary<(int Day, int Period), BoolVar>;

public static class BlockHcs
{
    public static (StartVarMap StartVarsByBlock, DayVarMap DayVarsByCourse)
        AddHc06_BlockSplit(
            CpModel model,
            XDict x,
            IReadOnlyList<Course> courses,
            IReadOnlyList<Room> rooms,
            SchedulePolicy schedulePolicy,
            LunchVarMap lunchBanVars,
            bool allowGraduateDaytimeOverflow)
    {
        var roomIds = rooms.Select(r => r.Id).ToList();
        var startVarsByBlock = new StartVarMap();
        var dayVarsByCourse = new DayVarMap();

        foreach (var c in courses)
        {
            if (c.IsFixed) continue;

            var blocks = c.BlockStructure.Count > 0
                ? c.BlockStructure.ToList()
                : new List<int> { c.HoursPerWeek };
            var fixedRids = c.FixedRooms.ToList();
            var academicPeriods = AcademicLevelTimePolicy.AllowedPeriods(
                c.Grade, allowGraduateDaytimeOverflow, schedulePolicy);

            var dayVars = new List<IntVar>();
            for (int i = 0; i < blocks.Count; i++)
            {
                int b = blocks[i];
                var dayVar = model.NewIntVar(0, Constants.Days - 1, $"day_{c.Id}_b{i}");
                dayVars.Add(dayVar);

                var validStarts = new List<(int d, int sp)>();
                var possibleStarts = SchedulePolicyRules.PossibleBlockStarts(
                    schedulePolicy, academicPeriods, b);
                for (int d = 0; d < Constants.Days; d++)
                    foreach (var sp in possibleStarts)
                        validStarts.Add((d, sp));

                if (validStarts.Count == 0)
                {
                    var infeasible = model.NewBoolVar($"__infeasible_{c.Id}_b{i}");
                    model.Add(infeasible == 1);
                    model.Add(infeasible == 0);
                    continue;
                }

                var startVars = new Dictionary<(int Day, int StartPeriod), BoolVar>();
                foreach (var (d, sp) in validStarts)
                    startVars[(d, sp)] = model.NewBoolVar($"start_{c.Id}_b{i}_{d}_{sp}");

                model.Add(LinearExpr.Sum(startVars.Values) == 1);

                foreach (var ((d, sp), svar) in startVars)
                {
                    foreach (var period in Enumerable.Range(sp, b))
                        if (lunchBanVars.TryGetValue((d, period), out var lunchBan))
                            model.Add(svar + lunchBan <= 1);

                    if (fixedRids.Count > 0)
                    {
                        foreach (var rid in fixedRids)
                            for (int k = 0; k < b; k++)
                                model.Add(x[(c.Id, d, sp + k, rid)] == 1).OnlyEnforceIf(svar);
                    }
                    else
                    {
                        var roomChoice = new Dictionary<string, BoolVar>();
                        foreach (var rid in roomIds)
                            roomChoice[rid] = model.NewBoolVar(
                                $"room_{c.Id}_b{i}_{d}_{sp}_{rid}");
                        model.Add(LinearExpr.Sum(roomChoice.Values) == 1).OnlyEnforceIf(svar);
                        model.Add(LinearExpr.Sum(roomChoice.Values) == 0).OnlyEnforceIf(svar.Not());
                        foreach (var (rid, rc) in roomChoice)
                            for (int k = 0; k < b; k++)
                                model.Add(x[(c.Id, d, sp + k, rid)] == 1).OnlyEnforceIf(rc);
                    }

                    model.Add(dayVar == d).OnlyEnforceIf(svar);
                }

                startVarsByBlock[(c.Id, i)] = startVars;
            }

            if (dayVars.Count > 0)
                dayVarsByCourse[c.Id] = dayVars;
        }

        return (startVarsByBlock, dayVarsByCourse);
    }

    public static void AddHc14_FixedRooms(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
    {
        foreach (var c in courses)
        {
            if (c.FixedRooms.Count == 0) continue;
            var allowed = new HashSet<string>(c.FixedRooms);
            for (int d = 0; d < Constants.Days; d++)
                foreach (var p in Constants.Periods)
                    foreach (var r in rooms)
                        if (!allowed.Contains(r.Id))
                            model.Add(x[(c.Id, d, p, r.Id)] == 0);
        }
    }

    public static void AddHc14_UnavailableRooms(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
    {
        var roomIds = rooms.Select(r => r.Id).ToHashSet();
        foreach (var c in courses)
        {
            if (c.UnavailableRooms.Count == 0) continue;
            foreach (var rid in c.UnavailableRooms.Where(roomIds.Contains))
                for (int d = 0; d < Constants.Days; d++)
                    foreach (var p in Constants.Periods)
                        model.Add(x[(c.Id, d, p, rid)] == 0);
        }
    }

    public static void AddHc21_ProfRoomEligibility(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms,
        Dictionary<string, Professor> profMap)
    {
        foreach (var c in courses)
        {
            if (c.FixedRooms.Count > 0) continue;
            if (!profMap.TryGetValue(c.ProfessorId, out var prof)) continue;

            var unavailable = prof.UnavailableRooms.ToHashSet();

            foreach (var r in rooms)
            {
                if (unavailable.Contains(r.Id))
                    for (int d = 0; d < Constants.Days; d++)
                        foreach (var p in Constants.Periods)
                            model.Add(x[(c.Id, d, p, r.Id)] == 0);
            }
        }
    }

    public static void AddHc22_AutoCourseRoomConsistent(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
    {
        foreach (var (_, sections) in ConstraintHelpers.GroupByBaseId(courses))
        {
            if (sections.Any(section => section.FixedRooms.Count > 0)) continue;

            var sharedRoom = rooms.ToDictionary(
                room => room.Id,
                room => model.NewBoolVar($"course_room_{sections[0].Id}_{room.Id}"));
            model.Add(LinearExpr.Sum(sharedRoom.Values) == 1);

            foreach (var section in sections)
                for (int d = 0; d < Constants.Days; d++)
                    foreach (var p in Constants.Periods)
                        foreach (var room in rooms)
                            model.Add(x[(section.Id, d, p, room.Id)] <= sharedRoom[room.Id]);
        }
    }

    public static void AddHc19_Len2StartPeriods(
        CpModel model, StartVarMap startVarsByBlock,
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses,
        SchedulePolicy schedulePolicy,
        LunchVarMap lunchBanVars,
        bool allowGraduateDaytimeOverflow)
    {
        // (1) length-2 block starts are derived from the effective schedule policy.
        foreach (var c in courses)
        {
            if (c.IsFixed) continue;
            var blocks = c.BlockStructure.Count > 0
                ? c.BlockStructure
                : new List<int> { c.HoursPerWeek };
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] != 2) continue;
                if (!startVarsByBlock.TryGetValue((c.Id, i), out var sv)) continue;
                var academicPeriods = AcademicLevelTimePolicy.AllowedPeriods(
                    c.Grade, allowGraduateDaytimeOverflow, schedulePolicy);
                var allowedStarts = SchedulePolicyRules.PossibleBlockStarts(
                    schedulePolicy, academicPeriods, 2).ToHashSet();
                foreach (var ((_, sp), v) in sv)
                    if (!allowedStarts.Contains(sp))
                        model.Add(v == 0);
            }
        }

        // (2) cross member section pair length-1 alignment
        var crossBaseIds = new HashSet<string>();
        if (crosses != null)
            foreach (var g in crosses)
                foreach (var bid in g.BaseIds)
                    crossBaseIds.Add(bid);
        if (crossBaseIds.Count == 0) return;

        var baseGroups = ConstraintHelpers.GroupByBaseId(courses);
        foreach (var (bid, group) in baseGroups)
        {
            if (!crossBaseIds.Contains(bid) || group.Count != 2) continue;
            var sorted = group.OrderBy(c => c.Section).ToList();
            var c1 = sorted[0];
            var c2 = sorted[1];
            if (c1.IsFixed || c2.IsFixed) continue;
            var blocks1 = c1.BlockStructure.Count > 0
                ? c1.BlockStructure : new List<int> { c1.HoursPerWeek };
            var blocks2 = c2.BlockStructure.Count > 0
                ? c2.BlockStructure : new List<int> { c2.HoursPerWeek };
            if (!blocks1.SequenceEqual(blocks2)) continue;

            for (int i = 0; i < blocks1.Count; i++)
            {
                if (blocks1[i] != 1) continue;
                if (!startVarsByBlock.TryGetValue((c1.Id, i), out var sv1)) continue;
                if (!startVarsByBlock.TryGetValue((c2.Id, i), out var sv2)) continue;
                var academicPeriods = AcademicLevelTimePolicy.AllowedPeriods(
                    c1.Grade, allowGraduateDaytimeOverflow, schedulePolicy);
                foreach (var ((d, sp), v1) in sv1)
                {
                    if (schedulePolicy.LunchMode == LunchPolicyMode.BanOneOfPeriods4And5)
                    {
                        foreach (var lunchPeriod in new[]
                                 {
                                     SchedulePolicyRules.FirstLunchCandidate,
                                     SchedulePolicyRules.SecondLunchCandidate,
                                 })
                        {
                            var periodsForChoice = academicPeriods
                                .Where(period => period != lunchPeriod)
                                .ToArray();
                            var partners = PairPartners(periodsForChoice);
                            var lunchBan = lunchBanVars[(d, lunchPeriod)];
                            if (partners.TryGetValue(sp, out var partnerSp)
                                && sv2.TryGetValue((d, partnerSp), out var v2))
                            {
                                var partnerConstraint = model.Add(v2 == 1);
                                partnerConstraint.OnlyEnforceIf(v1);
                                partnerConstraint.OnlyEnforceIf(lunchBan);
                            }
                            else
                            {
                                model.Add(v1 + lunchBan <= 1);
                            }
                        }
                    }
                    else
                    {
                        var partners = PairPartners(academicPeriods);
                        if (!partners.TryGetValue(sp, out var partnerSp)
                            || !sv2.TryGetValue((d, partnerSp), out var v2))
                            model.Add(v1 == 0);
                        else
                            model.Add(v2 == 1).OnlyEnforceIf(v1);
                    }
                }
            }
        }
    }

    private static IReadOnlyDictionary<int, int> PairPartners(IReadOnlyList<int> periods)
    {
        var periodSet = periods.ToHashSet();
        var result = new Dictionary<int, int>();
        foreach (var start in SchedulePolicyRules.DeriveTwoHourStarts(periods))
        {
            if (!periodSet.Contains(start + 1)) continue;
            result[start] = start + 1;
            result[start + 1] = start;
        }
        return result;
    }

    public static void AddHc20_BlockDaysDistinct(CpModel model, DayVarMap dayVarsByCourse)
    {
        foreach (var (_, dayVars) in dayVarsByCourse)
            if (dayVars.Count >= 2)
                model.AddAllDifferent(dayVars);
    }

    public static void AddHc15_SectionBackToBack(
        CpModel model, StartVarMap startVarsByBlock, IReadOnlyList<Course> courses)
    {
        var baseGroups = ConstraintHelpers.GroupByBaseId(courses);
        foreach (var (_, group) in baseGroups)
        {
            if (group.Count != 2) continue;
            var sorted = group.OrderBy(c => c.Section).ToList();
            var c1 = sorted[0];
            var c2 = sorted[1];
            if (c1.IsFixed || c2.IsFixed) continue;

            var profs1 = DomainHelpers.CourseProfIds(c1);
            var profs2 = DomainHelpers.CourseProfIds(c2);
            if (!profs1.SetEquals(profs2)) continue;
            if (profs1.Count == 0) continue;

            var blocks1 = c1.BlockStructure.Count > 0
                ? c1.BlockStructure : new List<int> { c1.HoursPerWeek };
            var blocks2 = c2.BlockStructure.Count > 0
                ? c2.BlockStructure : new List<int> { c2.HoursPerWeek };
            if (!blocks1.SequenceEqual(blocks2)) continue;

            for (int i = 0; i < blocks1.Count; i++)
            {
                int b = blocks1[i];
                if (b == 3) continue;
                if (!startVarsByBlock.TryGetValue((c1.Id, i), out var sv1)) continue;
                if (!startVarsByBlock.TryGetValue((c2.Id, i), out var sv2)) continue;
                var hasAnyAdjacentPair = sv1.Keys.Any(start =>
                    sv2.ContainsKey((start.Day, start.StartPeriod + b)) ||
                    sv2.ContainsKey((start.Day, start.StartPeriod - b)));
                if (!hasAnyAdjacentPair) continue;

                foreach (var ((d, sp), v1) in sv1)
                {
                    var candidates = new List<BoolVar>();
                    foreach (var delta in new[] { b, -b })
                    {
                        int sp2 = sp + delta;
                        if (sv2.TryGetValue((d, sp2), out var v2))
                            candidates.Add(v2);
                    }
                    if (candidates.Count == 0)
                        model.Add(v1 == 0);
                    else
                        model.Add(LinearExpr.Sum(candidates) >= 1).OnlyEnforceIf(v1);
                }
            }
        }
    }
}
