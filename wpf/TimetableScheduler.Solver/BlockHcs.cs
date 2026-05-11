using Google.OrTools.Sat;
using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

using XDict = Dictionary<(string CourseId, int Day, int Period, string RoomId), BoolVar>;
using StartVarMap = Dictionary<(string CourseId, int BlockIdx), Dictionary<(int Day, int StartPeriod), BoolVar>>;
using DayVarMap = Dictionary<string, List<IntVar>>;

public static class BlockHcs
{
    public static (StartVarMap StartVarsByBlock, DayVarMap DayVarsByCourse)
        AddHc06_BlockSplit(
            CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms)
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

            var dayVars = new List<IntVar>();
            for (int i = 0; i < blocks.Count; i++)
            {
                int b = blocks[i];
                var dayVar = model.NewIntVar(0, Constants.Days - 1, $"day_{c.Id}_b{i}");
                dayVars.Add(dayVar);

                var validStarts = new List<(int d, int sp)>();
                for (int d = 0; d < Constants.Days; d++)
                    foreach (var sp in Constants.ValidPeriods)
                    {
                        var blockPeriods = Enumerable.Range(sp, b).ToList();
                        if (blockPeriods.All(bp => Constants.ValidPeriods.Contains(bp)))
                            validStarts.Add((d, sp));
                    }

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
                foreach (var p in Constants.ValidPeriods)
                    foreach (var r in rooms)
                        if (!allowed.Contains(r.Id))
                            model.Add(x[(c.Id, d, p, r.Id)] == 0);
        }
    }

    public static void AddHc21_ProfRoomConsistent(
        CpModel model, XDict x, IReadOnlyList<Course> courses, IReadOnlyList<Room> rooms,
        Dictionary<string, Professor> profMap)
    {
        var autoCoursesByPid = new Dictionary<string, List<Course>>();
        foreach (var c in courses)
        {
            if (c.FixedRooms.Count > 0) continue;
            var pid = c.ProfessorId;
            if (string.IsNullOrEmpty(pid)) continue;
            if (!autoCoursesByPid.TryGetValue(pid, out var list))
                autoCoursesByPid[pid] = list = new List<Course>();
            list.Add(c);
        }

        var profRoom = new Dictionary<string, Dictionary<string, BoolVar>>();
        foreach (var pid in autoCoursesByPid.Keys)
        {
            profMap.TryGetValue(pid, out var prof);
            var allowed = prof?.AllowedRooms ?? new List<string>();
            var candidates = allowed.Count > 0 ? allowed : rooms.Select(r => r.Id).ToList();
            if (candidates.Count == 0) continue;
            var rdict = new Dictionary<string, BoolVar>();
            foreach (var rid in candidates)
                rdict[rid] = model.NewBoolVar($"prof_room_{pid}_{rid}");
            model.Add(LinearExpr.Sum(rdict.Values) == 1);
            profRoom[pid] = rdict;
        }

        foreach (var (pid, clist) in autoCoursesByPid)
        {
            if (!profRoom.TryGetValue(pid, out var rdict)) continue;
            foreach (var c in clist)
                for (int d = 0; d < Constants.Days; d++)
                    foreach (var p in Constants.ValidPeriods)
                        foreach (var r in rooms)
                        {
                            if (rdict.TryGetValue(r.Id, out var rv))
                                model.Add(x[(c.Id, d, p, r.Id)] <= rv);
                            else
                                model.Add(x[(c.Id, d, p, r.Id)] == 0);
                        }
        }
    }

    public static void AddHc18_BlockDayGap(CpModel model, DayVarMap dayVarsByCourse)
    {
        foreach (var (cid, dayVars) in dayVarsByCourse)
        {
            if (dayVars.Count < 2) continue;
            for (int i = 0; i < dayVars.Count; i++)
                for (int j = i + 1; j < dayVars.Count; j++)
                {
                    var diff = model.NewIntVar(
                        -(Constants.Days - 1), Constants.Days - 1, $"hc18_diff_{cid}_{i}_{j}");
                    var absDiff = model.NewIntVar(
                        0, Constants.Days - 1, $"hc18_abs_{cid}_{i}_{j}");
                    model.Add(diff == dayVars[i] - dayVars[j]);
                    model.AddAbsEquality(absDiff, diff);
                    model.Add(absDiff <= 2);
                }
        }
    }

    public static void AddHc19_Len2StartPeriods(
        CpModel model, StartVarMap startVarsByBlock,
        IReadOnlyList<Course> courses, IReadOnlyList<CrossGroup>? crosses = null)
    {
        // (1) length-2 blocks: start ∈ {1,3,6,8}
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
                foreach (var ((_, sp), v) in sv)
                    if (!Constants.Len2StartPeriods.Contains(sp))
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
                foreach (var ((d, sp), v1) in sv1)
                {
                    int partnerSp;
                    if (Constants.Len2StartPeriods.Contains(sp))
                        partnerSp = sp + 1;
                    else if (sp is 2 or 4 or 7 or 9)
                        partnerSp = sp - 1;
                    else
                        continue;

                    if (!sv2.TryGetValue((d, partnerSp), out var v2))
                        model.Add(v1 == 0);
                    else
                        model.Add(v2 == 1).OnlyEnforceIf(v1);
                }
            }
        }
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
                if (!startVarsByBlock.TryGetValue((c1.Id, i), out var sv1)) continue;
                if (!startVarsByBlock.TryGetValue((c2.Id, i), out var sv2)) continue;
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
