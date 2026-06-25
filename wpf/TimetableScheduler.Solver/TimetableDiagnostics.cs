using TimetableScheduler.Domain;

namespace TimetableScheduler.Solver;

public sealed record TimetableDiagnostic(string Id, string Message)
{
    public override string ToString() => $"{Id}: {Message}";
}

public static class TimetableDiagnostics
{
    private const string RequiredCourseType = "전필";

    public static IReadOnlyList<TimetableDiagnostic> GetInputErrors(
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<CrossGroup>? crosses = null,
        bool hasUnsavedEdits = false,
        string? unsavedEditSummary = null)
    {
        var issues = new List<TimetableDiagnostic>();
        AddBasicInputErrors(issues, courses, professors, rooms, crosses);

        if (hasUnsavedEdits)
        {
            var message = string.IsNullOrWhiteSpace(unsavedEditSummary)
                ? "완료되지 않은 편집 항목이 있습니다. 완료하거나 취소한 뒤 시간표를 생성하세요."
                : $"완료되지 않은 편집 항목이 있습니다: {unsavedEditSummary}. 각 항목에서 완료하거나 취소한 뒤 시간표를 생성하세요.";
            Add(issues, "IE-037", message);
        }

        return Distinct(issues);
    }

    public static IReadOnlyList<TimetableDiagnostic> GetGenerationErrors(
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<CrossGroup>? crosses = null,
        IReadOnlyList<RetakeScenario>? retakes = null,
        bool considerRetakeStudents = false)
    {
        var issues = new List<TimetableDiagnostic>();
        AddGenerationErrors(issues, courses, professors, rooms, crosses, retakes, considerRetakeStudents);
        return Distinct(issues);
    }

    private static void AddBasicInputErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<CrossGroup>? crosses)
    {
        var professorIds = professors.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var roomIds = rooms.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var course in courses)
        {
            var courseName = InputCourseLocation(course);
            if (string.IsNullOrWhiteSpace(course.Name))
                Add(issues, "IE-001", $"{courseName}: 과목명이 비어 있습니다. 과목명을 입력하세요.");

            if (string.IsNullOrWhiteSpace(course.ProfessorId))
                Add(issues, "IE-004", $"{courseName} 담당 교수가 비어 있습니다. 담당 교수를 선택하세요.");
            else if (!professorIds.Contains(course.ProfessorId))
                Add(issues, "IE-005", $"{courseName} 담당 교수({course.ProfessorId})를 찾을 수 없습니다. 교수를 다시 선택하세요.");

            foreach (var pid in course.CoteachProfs.Where(id => !string.IsNullOrWhiteSpace(id)))
                if (!professorIds.Contains(pid))
                    Add(issues, "IE-006", $"{courseName} 팀티칭 교수({pid})를 찾을 수 없습니다. 팀티칭 교수를 다시 선택하세요.");

            AddCourseShapeErrors(issues, course, "IE");

            if (rooms.Count == 0)
                continue;

            if (course.UnavailableRooms.Intersect(roomIds, StringComparer.Ordinal).Count() >= rooms.Count)
                Add(issues, "IE-019", $"{courseName} 과목은 모든 강의실이 불가로 설정되어 있습니다. 불가강의실을 줄이세요.");

            var fixedUnavailable = course.FixedRooms.Intersect(course.UnavailableRooms, StringComparer.Ordinal).ToList();
            if (fixedUnavailable.Count > 0)
                Add(issues, "IE-020", $"{courseName} 과목의 고정강의실과 불가강의실이 겹칩니다: {string.Join(", ", fixedUnavailable)}");

            foreach (var rid in course.FixedRooms.Where(id => !roomIds.Contains(id)).Distinct(StringComparer.Ordinal))
                Add(issues, "IE-021", $"{courseName} 고정강의실({rid})을 찾을 수 없습니다. 강의실을 다시 선택하세요.");

            foreach (var rid in course.UnavailableRooms.Where(id => !roomIds.Contains(id)).Distinct(StringComparer.Ordinal))
                Add(issues, "IE-022", $"{courseName} 불가강의실({rid})을 찾을 수 없습니다. 해당 항목을 제거하세요.");
        }

        foreach (var professor in professors)
            if (string.IsNullOrWhiteSpace(professor.Name))
                Add(issues, "IE-002", $"{InputProfessorLocation(professor)}: 교수명이 비어 있습니다. 교수명을 입력하세요.");

        foreach (var room in rooms)
            if (string.IsNullOrWhiteSpace(room.Name))
                Add(issues, "IE-003", $"{InputRoomLocation(room)}: 강의실명이 비어 있습니다. 강의실명을 입력하세요.");

        if (rooms.Count == 0)
            Add(issues, "IE-027", "강의실 관리: 강의실이 하나도 없습니다. 시간표 생성을 위해 강의실을 하나 이상 추가하세요.");

        AddFixedOverlapInputErrors(issues, courses, crosses);
        AddRoomAndTimeCandidateInputErrors(issues, courses, professors, rooms);
        AddSectionAdjacencyErrors(issues, courses, professors, "IE-041", inputLocation: true);
        AddCrossInputErrors(issues, courses, crosses);
        AddGradeSlotCapacityErrors(issues, courses, crosses, "IE-038");
    }

    private static void AddGenerationErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms,
        IReadOnlyList<CrossGroup>? crosses,
        IReadOnlyList<RetakeScenario>? retakes,
        bool considerRetakeStudents)
    {
        foreach (var course in courses)
        {
            var courseName = CourseLabel(course);
            if (course.IsFixed && course.FixedSlots.Count == 0)
                Add(issues, "GE-001", $"{courseName} 고정 시간이 비어 있습니다. 시간고정을 해제하거나 시간을 선택하세요.");

            if (course.IsFixed && course.FixedSlots.Any(slot => slot.Period == Constants.LunchPeriod))
                Add(issues, "GE-002", $"{courseName} 고정 시간이 점심시간 5교시를 포함합니다. 다른 교시로 변경하세요.");

            if (course.IsFixed && course.FixedSlots.Any(slot => !IsAllowedPeriod(course, slot.Period)))
                Add(issues, "GE-029", $"{courseName} 고정 시간이 수업 가능 시간대와 맞지 않습니다. 대학원 과목은 야간 10~13교시, 학부 과목은 주간 1~9교시(점심 제외)만 선택하세요.");

            if (course.BlockStructure.Count > 0 && course.BlockStructure.Sum() != course.HoursPerWeek)
                Add(issues, "GE-003", $"{courseName} 블록구조 합계가 주당 수업시간과 다릅니다. 블록구조 또는 시수를 수정하세요.");

            if (rooms.Count > 0 && CandidateRooms(course, null, rooms).Count == 0)
                Add(issues, "GE-004", $"{courseName} 과목에 사용 가능한 강의실이 없습니다. 불가강의실/고정강의실을 수정하세요.");

            foreach (var block in EffectiveBlocks(course))
            {
                if (block > MaxContiguousValidPeriods(course))
                    Add(issues, "GE-013", $"{courseName} 블록 길이 {block}시간은 하루 안에 배치할 수 없습니다. 블록구조를 수정하세요.");
            }

            if (!course.IsFixed && EffectiveBlocks(course).Count > Constants.Days)
                Add(issues, "GE-014", $"{courseName} 블록 수가 수업 가능 요일보다 많습니다. 블록구조를 줄이세요.");
        }

        var professorMap = professors.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var course in courses)
        {
            foreach (var pid in DomainHelpers.CourseProfIds(course))
            {
                if (!professorMap.TryGetValue(pid, out var professor))
                    continue;

                if (rooms.Count > 0 && CandidateRooms(course, professor, rooms).Count == 0)
                    Add(issues, "GE-005", $"{CourseLabel(course)} / {professor.Name} 조건을 만족하는 강의실이 없습니다. 교수 강의실 조건 또는 과목 불가강의실을 수정하세요.");

                if (course.IsFixed && course.FixedSlots.Any(slot => IsUnavailable(professor, slot)))
                    Add(issues, "GE-006", $"{CourseLabel(course)} 고정 시간이 {professor.Name} 교수의 불가 시간과 겹칩니다. 불가 시간 또는 시간고정을 수정하세요.");
            }
        }

        AddFixedOverlapGenerationErrors(issues, courses, crosses);
        AddTeamTeachingGenerationErrors(issues, courses, professors, rooms);
        AddSectionAdjacencyErrors(issues, courses, professors, "GE-030", inputLocation: false);
        AddCrossGenerationErrors(issues, courses, crosses);
        AddGraduateThreeHourBlockCapacityErrors(issues, courses, crosses);
        AddGradeSlotCapacityErrors(issues, courses, crosses, "GE-027");
        AddRetakeGenerationErrors(issues, courses, crosses, retakes, considerRetakeStudents);
    }

    private static void AddCourseShapeErrors(List<TimetableDiagnostic> issues, Course course, string prefix)
    {
        var courseName = prefix == "IE" ? InputCourseLocation(course) : CourseLabel(course);
        if (course.BlockStructure.Count > 0)
        {
            if (course.BlockStructure.Sum() != course.HoursPerWeek)
                Add(issues, $"{prefix}-007", $"{courseName} 블록구조 합계가 주당 수업시간과 다릅니다. 블록구조 또는 시수를 수정하세요.");
            else if (!IsAllowedBlockStructure(course.BlockStructure, course.HoursPerWeek))
                Add(issues, $"{prefix}-008", $"{courseName} 블록구조는 {AllowedBlockText(course.HoursPerWeek)} 중 하나여야 합니다.");
        }

        if (course.IsFixed)
        {
            if (course.FixedSlots.Count == 0)
                Add(issues, $"{prefix}-010", $"{courseName} 시간고정이 켜졌지만 고정 시간이 비어 있습니다. 시간을 선택하세요.");

            if (course.FixedSlots.Any(slot => slot.Period == Constants.LunchPeriod))
                Add(issues, $"{prefix}-011", $"{courseName} 고정 시간이 점심시간 5교시를 포함합니다. 다른 교시를 선택하세요.");

            if (course.FixedSlots.Any(slot => !IsAllowedPeriod(course, slot.Period)))
                Add(issues, $"{prefix}-040", $"{courseName} 고정 시간이 수업 가능 시간대와 맞지 않습니다. 대학원 과목은 야간 10~13교시, 학부 과목은 주간 1~9교시(점심 제외)만 선택하세요.");

            if (course.FixedSlots.Count > 0 && course.FixedSlots.Count != course.HoursPerWeek)
                Add(issues, $"{prefix}-012", $"{courseName} 고정 시간 개수가 주당 수업시간과 다릅니다. 고정 시간을 다시 선택하세요.");

            if (course.FixedSlots.Count > 0 && !FixedSlotsMatchBlocks(course))
                Add(issues, $"{prefix}-013", $"{courseName} 고정 시간이 블록구조와 맞지 않습니다. 시간고정 또는 블록구조를 수정하세요.");
        }
    }

    private static void AddFixedOverlapInputErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses)
    {
        foreach (var (first, second) in CoursePairs(courses))
        {
            var overlaps = FixedOverlaps(first, second).ToList();
            if (overlaps.Count == 0) continue;

            if (SharesGradeConstraint(first, second, crosses))
                Add(issues, "IE-016", $"{InputCoursePair(first, second)}: 같은 학년에서 고정 시간이 겹칩니다({SlotLabels(overlaps)}). 시간고정을 수정하세요.");

            var sharedProfessors = DomainHelpers.CourseProfIds(first).Intersect(DomainHelpers.CourseProfIds(second), StringComparer.Ordinal).ToList();
            if (sharedProfessors.Count > 0)
            {
                var id = first.CoteachProfs.Count > 0 || second.CoteachProfs.Count > 0 ? "IE-018" : "IE-017";
                Add(issues, id, $"{InputCoursePair(first, second)} / {InputProfessorLocations(sharedProfessors)}: 공통 교수의 고정 시간이 겹칩니다({SlotLabels(overlaps)}). 시간고정을 수정하세요.");
            }
        }
    }

    private static void AddFixedOverlapGenerationErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses)
    {
        foreach (var (first, second) in CoursePairs(courses))
        {
            var overlaps = FixedOverlaps(first, second).ToList();
            if (overlaps.Count == 0) continue;

            var sharedFixedRooms = first.FixedRooms.Intersect(second.FixedRooms, StringComparer.Ordinal).ToList();
            if (sharedFixedRooms.Count > 0)
                Add(issues, "GE-007", $"{CourseLabel(first)} 과목과 {CourseLabel(second)} 과목이 같은 강의실/시간에 고정되어 있습니다. 시간 또는 강의실을 변경하세요.");

            var sharedProfessors = DomainHelpers.CourseProfIds(first).Intersect(DomainHelpers.CourseProfIds(second), StringComparer.Ordinal).ToList();
            if (sharedProfessors.Count > 0)
            {
                var id = first.CoteachProfs.Count > 0 || second.CoteachProfs.Count > 0 ? "GE-010" : "GE-008";
                Add(issues, id, $"{CourseLabel(first)} 과목과 {CourseLabel(second)} 과목이 같은 교수/시간에 고정되어 있습니다. 시간 또는 교수 배정을 변경하세요.");
            }

            if (SharesGradeConstraint(first, second, crosses))
                Add(issues, "GE-009", $"{CourseLabel(first)} 과목과 {CourseLabel(second)} 과목은 같은 학년에서 반드시 겹칩니다. 시간고정 또는 학년/분반 정보를 수정하세요.");
        }
    }

    private static void AddRoomAndTimeCandidateInputErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms)
    {
        var professorMap = professors.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var course in courses)
        {
            foreach (var pid in DomainHelpers.CourseProfIds(course))
            {
                if (!professorMap.TryGetValue(pid, out var professor))
                    continue;

                if (rooms.Count > 0 && CandidateRooms(course, professor, rooms).Count == 0)
                    Add(issues, "IE-023", $"{InputCourseLocation(course)} / {InputProfessorLocation(professor)}: 조건으로 사용 가능한 강의실이 없습니다. 교수 강의실 조건 또는 과목 불가강의실을 수정하세요.");

                var requiredSlots = RequiredSlotCount(course);
                var availableSlots = AllowedPeriods(course)
                    .SelectMany(period => Enumerable.Range(0, Constants.Days).Select(day => new TimeSlot(day, period)))
                    .Count(slot => !IsUnavailable(professor, slot));
                if (!course.IsFixed && availableSlots < requiredSlots)
                    Add(issues, "IE-024", $"{InputCourseLocation(course)} / {InputProfessorLocation(professor)}: 가용 시간이 부족합니다. 교수 불가 시간을 줄이세요.");
            }

            if (course.CoteachProfs.Count > 0)
            {
                var teachingProfessors = DomainHelpers.CourseProfIds(course)
                    .Select(id => professorMap.TryGetValue(id, out var professor) ? professor : null)
                    .Where(professor => professor != null)
                    .Cast<Professor>()
                    .ToList();
                if (teachingProfessors.Count >= 2)
                {
                    var requiredSlots = RequiredSlotCount(course);
                    var commonSlots = AllowedPeriods(course)
                        .SelectMany(period => Enumerable.Range(0, Constants.Days).Select(day => new TimeSlot(day, period)))
                        .Count(slot => teachingProfessors.All(professor => !IsUnavailable(professor, slot)));
                    if (!course.IsFixed && commonSlots < requiredSlots)
                        Add(issues, "IE-025", $"{InputCourseLocation(course)} / {string.Join(", ", teachingProfessors.Select(InputProfessorLocation))}: 팀티칭 교수들의 공통 가능 시간이 부족합니다. 교수 불가 시간을 조정하세요.");

                    if (rooms.Count > 0 && CommonCandidateRooms(course, teachingProfessors, rooms).Count == 0)
                        Add(issues, "IE-026", $"{InputCourseLocation(course)} / {string.Join(", ", teachingProfessors.Select(InputProfessorLocation))}: 팀티칭 교수들의 공통 가능 강의실이 없습니다. 교수 강의실 조건을 조정하세요.");
                }
            }
        }
    }

    private static void AddTeamTeachingGenerationErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms)
    {
        var professorMap = professors.ToDictionary(p => p.Id, StringComparer.Ordinal);
        foreach (var course in courses.Where(c => c.CoteachProfs.Count > 0))
        {
            var teachingProfessors = DomainHelpers.CourseProfIds(course)
                .Select(id => professorMap.TryGetValue(id, out var professor) ? professor : null)
                .Where(professor => professor != null)
                .Cast<Professor>()
                .ToList();
            if (teachingProfessors.Count < 2) continue;

            var requiredSlots = RequiredSlotCount(course);
            var commonSlots = AllowedPeriods(course)
                .SelectMany(period => Enumerable.Range(0, Constants.Days).Select(day => new TimeSlot(day, period)))
                .Count(slot => teachingProfessors.All(professor => !IsUnavailable(professor, slot)));
            if (!course.IsFixed && commonSlots < requiredSlots)
                Add(issues, "GE-011", $"{CourseLabel(course)} 팀티칭 교수들의 공통 가능 시간이 부족합니다. 교수 불가 시간을 수정하세요.");

            if (rooms.Count > 0 && CommonCandidateRooms(course, teachingProfessors, rooms).Count == 0)
                Add(issues, "GE-012", $"{CourseLabel(course)} 팀티칭 교수들의 공통 가능 강의실이 없습니다. 교수 강의실 조건을 수정하세요.");
        }
    }

    private static void AddSectionAdjacencyErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<Professor> professors,
        string id,
        bool inputLocation)
    {
        var professorMap = professors.ToDictionary(professor => professor.Id, StringComparer.Ordinal);
        foreach (var sections in GroupByBaseId(courses).Values)
        {
            if (sections.Count != 2) continue;
            var first = sections[0];
            var second = sections[1];
            if (first.IsFixed || second.IsFixed) continue;

            var firstProfIds = DomainHelpers.CourseProfIds(first);
            var secondProfIds = DomainHelpers.CourseProfIds(second);
            if (!firstProfIds.SetEquals(secondProfIds) || firstProfIds.Count == 0) continue;

            var firstBlocks = EffectiveBlocks(first);
            var secondBlocks = EffectiveBlocks(second);
            if (!firstBlocks.SequenceEqual(secondBlocks)) continue;

            if (firstProfIds.Any(professorId => !professorMap.ContainsKey(professorId)))
                continue;

            for (var index = 0; index < firstBlocks.Count; index++)
            {
                var block = firstBlocks[index];
                if (block == 3) continue;

                var potentialFirst = PotentialBlockStarts(first, block);
                var potentialSecond = PotentialBlockStarts(second, block);
                if (!HasAdjacentStartPair(potentialFirst, potentialSecond, block))
                    continue;

                var feasibleFirst = FeasibleBlockStarts(first, block, professorMap);
                var feasibleSecond = FeasibleBlockStarts(second, block, professorMap);
                if (feasibleFirst.Count == 0 || feasibleSecond.Count == 0)
                    continue;
                if (HasAdjacentStartPair(feasibleFirst, feasibleSecond, block))
                    continue;

                var courseLabel = inputLocation
                    ? InputCoursePair(first, second)
                    : $"{CourseLabel(first)} / {CourseLabel(second)}";
                var professorLabels = string.Join(", ", firstProfIds
                    .OrderBy(professorId => professorId, StringComparer.Ordinal)
                    .Select(professorId => ProfessorLabel(professorMap[professorId])));
                Add(
                    issues,
                    id,
                    $"{courseLabel}: 같은 교수({professorLabels})의 분반은 {block}시간 블록을 같은 날 인접하게 배치해야 하지만 가능한 인접 시간쌍이 없습니다. 교수 불가 시간을 줄이거나 담당 교수를 분리하세요.");
            }
        }
    }

    private static void AddCrossInputErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses)
    {
        if (crosses == null || crosses.Count == 0) return;
        var groups = GroupByBaseId(courses);

        foreach (var cross in crosses)
        {
            if (cross.BaseIds.Any(id => !groups.ContainsKey(id)))
                Add(issues, "IE-033", $"{InputCrossLocation(cross)}: 대상 과목을 찾을 수 없습니다. Cross를 삭제하고 다시 등록하세요.");

            if (CrossBlockStructureConflict(cross, groups))
                Add(issues, "IE-035", $"{InputCrossLocation(cross)}: Cross 과목들의 블록구조가 다릅니다. 블록구조를 같게 맞추거나 Cross를 해제하세요.");

            if (CrossFixedConflict(cross, groups))
                Add(issues, "IE-034", $"{InputCrossLocation(cross)}: Cross와 시간고정 조건을 동시에 만족할 수 없습니다. Cross 또는 시간고정을 수정하세요.");

            if (CrossFixedRoomConflict(cross, groups))
                Add(issues, "IE-039", $"{InputCrossLocation(cross)}: Cross 과목들이 공통 고정 강의실을 사용하므로 같은 시간에 배치할 수 없습니다. Cross 또는 고정 강의실을 수정하세요.");
        }
    }

    private static void AddCrossGenerationErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses)
    {
        if (crosses == null || crosses.Count == 0) return;
        var groups = GroupByBaseId(courses);

        foreach (var cross in crosses)
        {
            if (cross.BaseIds.Count != 2)
            {
                Add(issues, "GE-016", $"{cross.Id} Cross는 과목 2개만 포함해야 합니다. Cross를 다시 설정하세요.");
                continue;
            }

            var groupedCourses = cross.BaseIds
                .Select(id => groups.TryGetValue(id, out var group) ? group : new List<Course>())
                .ToList();
            if (groupedCourses.Any(group => group.Count == 0))
            {
                Add(issues, "GE-017", $"{cross.Id} Cross 대상 과목을 찾을 수 없습니다. Cross를 삭제하고 다시 등록하세요.");
                continue;
            }

            if (groupedCourses.Select(group => group.Count).Distinct().Count() > 1)
                Add(issues, "GE-018", $"{cross.Id} Cross 과목들의 분반 수가 다릅니다. 분반 수를 맞추세요.");

            if (groupedCourses.Select(group => group[0].HoursPerWeek).Distinct().Count() > 1)
                Add(issues, "GE-019", $"{cross.Id} Cross 과목들의 총 시수가 다릅니다. 시수를 맞추세요.");

            if (CrossBlockStructureConflict(cross, groups))
                Add(issues, "GE-026", $"{cross.Id} Cross 과목들의 블록구조가 다릅니다. 블록구조를 같게 맞추거나 Cross를 해제하세요.");

            if (CrossFixedConflict(cross, groups))
                Add(issues, "GE-020", $"{cross.Id} Cross와 시간고정 조건이 충돌합니다. Cross를 해제하거나 시간고정을 수정하세요.");

            if (CrossFixedRoomConflict(cross, groups))
                Add(issues, "GE-028", $"{cross.Id} Cross 과목들이 공통 고정 강의실을 사용하므로 같은 시간에 배치할 수 없습니다. Cross를 해제하거나 고정 강의실을 수정하세요.");
        }
    }

    private static void AddGradeSlotCapacityErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses,
        string id)
    {
        var baseRequirements = BaseGradeSlotRequirements(courses);
        var requiredByGrade = baseRequirements
            .GroupBy(pair => pair.Key.Grade)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(pair => pair.Value));
        var crossOverlapByGrade = CrossSlotOverlapByGrade(crosses, baseRequirements);

        foreach (var grade in requiredByGrade.Keys.OrderBy(grade => grade))
        {
            var capacity = Constants.Days * AllowedPeriods(grade).Count;
            var crossOverlap = crossOverlapByGrade.TryGetValue(grade, out var overlap) ? overlap : 0;
            var requiredSlots = Math.Max(0, requiredByGrade[grade] - crossOverlap);
            if (requiredSlots <= capacity) continue;

            Add(
                issues,
                id,
                $"교과목 관리 > {AcademicLevels.DisplayName(grade)}: 과목들이 사용할 수 있는 시간칸 {capacity}칸을 초과합니다. 필요한 최소 시간칸은 Cross 반영 후 {requiredSlots}칸입니다. 과목 시수/분반 수를 줄이거나 Cross를 추가해 같은 시간 배치를 허용하세요.");
        }
    }

    private static void AddGraduateThreeHourBlockCapacityErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses)
    {
        var graduateBaseIds = courses
            .Where(course => course.Grade == AcademicLevels.GraduateGrade)
            .Select(course => DomainHelpers.BaseId(course.Id))
            .ToHashSet(StringComparer.Ordinal);
        var hasGraduateCross = crosses?.Any(cross =>
            cross.BaseIds.Count == 2 &&
            cross.BaseIds.All(graduateBaseIds.Contains)) == true;
        if (hasGraduateCross) return;

        var threeHourBlocks = courses
            .Where(course => course.Grade == AcademicLevels.GraduateGrade)
            .Sum(course => EffectiveBlocks(course).Count(block => block == 3));
        if (threeHourBlocks <= Constants.Days) return;

        Add(
            issues,
            "GE-031",
            $"교과목 관리 > 대학원: 3시간 연속 수업 블록이 {threeHourBlocks}개입니다. 야간은 하루 4교시뿐이고 대학원 수업끼리는 겹칠 수 없어 최대 {Constants.Days}개만 배치할 수 있습니다. 과목 수/블록구조를 줄이거나 Cross를 설정하세요.");
    }

    private static void AddRetakeGenerationErrors(
        List<TimetableDiagnostic> issues,
        IReadOnlyList<Course> courses,
        IReadOnlyList<CrossGroup>? crosses,
        IReadOnlyList<RetakeScenario>? manualRetakes,
        bool considerRetakeStudents)
    {
        if (!considerRetakeStudents) return;

        var retakes = EffectiveRetakes(courses, manualRetakes);
        var groups = GroupByBaseId(courses);
        foreach (var retake in retakes)
        {
            if (!groups.TryGetValue(retake.RetakeBaseId, out var retakeSections) || retakeSections.Count == 0)
                continue;

            var currentMajors = courses
                .Where(course =>
                    course.Grade == retake.CurrentGrade &&
                    IsRequiredMajor(course) &&
                    DomainHelpers.BaseId(course.Id) != retake.RetakeBaseId)
                .ToList();
            if (currentMajors.Count == 0) continue;

            bool allFixed = retakeSections.All(section => section.IsFixed && section.FixedSlots.Count > 0)
                && currentMajors.All(course => course.IsFixed && course.FixedSlots.Count > 0);
            if (!allFixed) continue;

            bool hasSafeSection = retakeSections.Any(section =>
                currentMajors.All(major =>
                    !section.FixedSlots.Any(slot => major.FixedSlots.Contains(slot))));
            if (!hasSafeSection)
                Add(issues, "GE-021", $"{AcademicLevels.DisplayName(retake.CurrentGrade)} 재수강생 고려 조건 때문에 {retake.RetakeBaseId} 전필 안전 분반을 만들 수 없습니다. 재수강생 고려를 해제하거나 전필 시간 조건을 완화하세요.");
        }
    }

    private static IReadOnlyList<RetakeScenario> EffectiveRetakes(
        IReadOnlyList<Course> courses,
        IReadOnlyList<RetakeScenario>? manualRetakes)
    {
        var byKey = new Dictionary<(int Grade, string BaseId), RetakeScenario>();
        foreach (var retake in DomainHelpers.DeriveAutoRetakes(courses))
            byKey[(retake.CurrentGrade, retake.RetakeBaseId)] = retake;

        if (manualRetakes != null)
            foreach (var retake in manualRetakes)
                byKey[(retake.CurrentGrade, retake.RetakeBaseId)] = retake;

        return byKey.Values.ToList();
    }

    private static IReadOnlyList<Room> CandidateRooms(Course course, Professor? professor, IReadOnlyList<Room> rooms)
    {
        return rooms.Where(room =>
            !course.UnavailableRooms.Contains(room.Id) &&
            (course.FixedRooms.Count == 0 || course.FixedRooms.Contains(room.Id)) &&
            (professor == null || !professor.UnavailableRooms.Contains(room.Id)))
            .ToList();
    }

    private static IReadOnlyList<Room> CommonCandidateRooms(
        Course course,
        IReadOnlyList<Professor> professors,
        IReadOnlyList<Room> rooms)
    {
        return rooms.Where(room =>
            !course.UnavailableRooms.Contains(room.Id) &&
            (course.FixedRooms.Count == 0 || course.FixedRooms.Contains(room.Id)) &&
            professors.All(professor => !professor.UnavailableRooms.Contains(room.Id)))
            .ToList();
    }

    private static bool IsUnavailable(Professor professor, TimeSlot slot) =>
        professor.UnavailableSlots.Any(unavailable => unavailable.Day == slot.Day && unavailable.Period == slot.Period);

    private static bool CrossFixedConflict(CrossGroup cross, IReadOnlyDictionary<string, List<Course>> groups)
    {
        if (cross.BaseIds.Count != 2) return false;
        if (!groups.TryGetValue(cross.BaseIds[0], out var first)) return false;
        if (!groups.TryGetValue(cross.BaseIds[1], out var second)) return false;
        if (first.Count != second.Count || first.Count == 0) return false;

        first = first.OrderBy(course => course.Section).ToList();
        second = second.OrderBy(course => course.Section).ToList();
        for (var index = 0; index < first.Count; index++)
        {
            var left = first[index];
            var right = second[(index + 1) % second.Count];
            if (!left.IsFixed || !right.IsFixed) continue;
            if (!SlotSet(left.FixedSlots).SetEquals(right.FixedSlots))
                return true;
        }
        return false;
    }

    private static bool CrossFixedRoomConflict(CrossGroup cross, IReadOnlyDictionary<string, List<Course>> groups)
    {
        if (cross.BaseIds.Count != 2) return false;
        if (!groups.TryGetValue(cross.BaseIds[0], out var first)) return false;
        if (!groups.TryGetValue(cross.BaseIds[1], out var second)) return false;
        if (first.Count != second.Count || first.Count == 0) return false;

        first = first.OrderBy(course => course.Section).ToList();
        second = second.OrderBy(course => course.Section).ToList();
        for (var index = 0; index < first.Count; index++)
        {
            var left = first[index];
            var right = second[(index + 1) % second.Count];
            if (left.FixedRooms.Intersect(right.FixedRooms, StringComparer.Ordinal).Any())
                return true;
        }
        return false;
    }

    private static bool CrossBlockStructureConflict(CrossGroup cross, IReadOnlyDictionary<string, List<Course>> groups)
    {
        if (cross.BaseIds.Count != 2) return false;
        if (!groups.TryGetValue(cross.BaseIds[0], out var first)) return false;
        if (!groups.TryGetValue(cross.BaseIds[1], out var second)) return false;
        if (first.Count != second.Count || first.Count == 0) return false;

        first = first.OrderBy(course => course.Section).ToList();
        second = second.OrderBy(course => course.Section).ToList();
        for (var index = 0; index < first.Count; index++)
        {
            var left = first[index];
            var right = second[(index + 1) % second.Count];
            if (!EffectiveBlocks(left).SequenceEqual(EffectiveBlocks(right)))
                return true;
        }
        return false;
    }

    private static IEnumerable<TimeSlot> FixedOverlaps(Course first, Course second)
    {
        if (!first.IsFixed || !second.IsFixed) yield break;
        foreach (var slot in first.FixedSlots)
            if (second.FixedSlots.Contains(slot))
                yield return slot;
    }

    private static IEnumerable<(Course First, Course Second)> CoursePairs(IReadOnlyList<Course> courses)
    {
        for (var i = 0; i < courses.Count; i++)
            for (var j = i + 1; j < courses.Count; j++)
                yield return (courses[i], courses[j]);
    }

    private static bool SharesGradeConstraint(Course first, Course second, IReadOnlyList<CrossGroup>? crosses)
    {
        if (first.Grade != second.Grade) return false;
        var firstBase = DomainHelpers.BaseId(first.Id);
        var secondBase = DomainHelpers.BaseId(second.Id);
        if (firstBase == secondBase) return false;
        return crosses == null || !crosses.Any(cross => cross.BaseIds.Contains(firstBase) && cross.BaseIds.Contains(secondBase));
    }

    private static Dictionary<string, List<Course>> GroupByBaseId(IReadOnlyList<Course> courses) =>
        courses
            .GroupBy(course => DomainHelpers.BaseId(course.Id))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(course => course.Section).ToList(),
                StringComparer.Ordinal);

    private static Dictionary<(int Grade, string BaseId), int> BaseGradeSlotRequirements(IReadOnlyList<Course> courses) =>
        courses
            .Where(course => course.Grade > 0)
            .GroupBy(course => (course.Grade, BaseId: DomainHelpers.BaseId(course.Id)))
            .ToDictionary(
                group => group.Key,
                group => group.Sum(RequiredSlotCount));

    private static Dictionary<int, int> CrossSlotOverlapByGrade(
        IReadOnlyList<CrossGroup>? crosses,
        IReadOnlyDictionary<(int Grade, string BaseId), int> baseRequirements)
    {
        var result = new Dictionary<int, int>();
        if (crosses == null || crosses.Count == 0) return result;

        var seenPairs = new HashSet<(int Grade, string FirstBaseId, string SecondBaseId)>();
        foreach (var cross in crosses)
        {
            if (cross.BaseIds.Count != 2) continue;

            var leftBaseId = cross.BaseIds[0];
            var rightBaseId = cross.BaseIds[1];
            if (string.Equals(leftBaseId, rightBaseId, StringComparison.Ordinal))
                continue;

            foreach (var left in baseRequirements.Where(pair => pair.Key.BaseId == leftBaseId))
            {
                if (!baseRequirements.TryGetValue((left.Key.Grade, rightBaseId), out var rightRequired))
                    continue;

                var firstBaseId = string.CompareOrdinal(leftBaseId, rightBaseId) <= 0 ? leftBaseId : rightBaseId;
                var secondBaseId = string.CompareOrdinal(leftBaseId, rightBaseId) <= 0 ? rightBaseId : leftBaseId;
                if (!seenPairs.Add((left.Key.Grade, firstBaseId, secondBaseId)))
                    continue;

                var overlap = Math.Min(left.Value, rightRequired);
                result[left.Key.Grade] = result.TryGetValue(left.Key.Grade, out var previous)
                    ? previous + overlap
                    : overlap;
            }
        }

        return result;
    }

    private static bool FixedSlotsMatchBlocks(Course course)
    {
        var blocks = EffectiveBlocks(course).OrderBy(x => x).ToList();
        var runs = course.FixedSlots
            .GroupBy(slot => slot.Day)
            .SelectMany(dayGroup =>
            {
                var periods = dayGroup.Select(slot => slot.Period).Distinct().OrderBy(period => period).ToList();
                var lengths = new List<int>();
                var index = 0;
                while (index < periods.Count)
                {
                    var length = 1;
                    while (index + length < periods.Count && periods[index + length] == periods[index] + length)
                        length++;
                    lengths.Add(length);
                    index += length;
                }
                return lengths;
            })
            .OrderBy(x => x)
            .ToList();

        return runs.SequenceEqual(blocks);
    }

    private static List<int> EffectiveBlocks(Course course) =>
        course.BlockStructure.Count > 0 ? course.BlockStructure.ToList() : new List<int> { course.HoursPerWeek };

    private static int RequiredSlotCount(Course course) =>
        Math.Max(course.HoursPerWeek, course.BlockStructure.Count > 0 ? course.BlockStructure.Sum() : course.HoursPerWeek);

    private static bool IsAllowedBlockStructure(IReadOnlyList<int> blocks, int weeklyHours)
    {
        var text = string.Join("+", blocks);
        return weeklyHours switch
        {
            <= 1 => text == "1",
            2 => text == "2",
            3 => text is "1+2" or "3",
            4 => text is "2+2" or "4",
            5 => text is "1+2+2" or "2+3",
            _ => text == weeklyHours.ToString(),
        };
    }

    private static string AllowedBlockText(int weeklyHours) => weeklyHours switch
    {
        <= 1 => "1",
        2 => "2",
        3 => "1+2 또는 3",
        4 => "2+2 또는 4",
        5 => "1+2+2 또는 2+3",
        _ => weeklyHours.ToString(),
    };

    private static int MaxContiguousValidPeriods(Course course)
    {
        var max = 0;
        var current = 0;
        var previous = 0;
        foreach (var period in AllowedPeriods(course))
        {
            current = period == previous + 1 ? current + 1 : 1;
            max = Math.Max(max, current);
            previous = period;
        }
        return max;
    }

    private static IReadOnlyList<int> AllowedPeriods(Course course) =>
        AllowedPeriods(course.Grade);

    private static IReadOnlyList<int> AllowedPeriods(int grade) =>
        grade == AcademicLevels.GraduateGrade ? Constants.NightPeriods : Constants.DaytimePeriods;

    private static bool IsAllowedPeriod(Course course, int period) =>
        AllowedPeriods(course).Contains(period);

    private static HashSet<(int Day, int StartPeriod)> PotentialBlockStarts(Course course, int block)
    {
        var starts = new HashSet<(int Day, int StartPeriod)>();
        for (var day = 0; day < Constants.Days; day++)
            foreach (var start in AllowedPeriods(course))
            {
                if (block == 2 && !Constants.Len2StartPeriods.Contains(start))
                    continue;

                var periods = Enumerable.Range(start, block).ToList();
                if (periods.All(period => AllowedPeriods(course).Contains(period)))
                    starts.Add((day, start));
            }
        return starts;
    }

    private static HashSet<(int Day, int StartPeriod)> FeasibleBlockStarts(
        Course course,
        int block,
        IReadOnlyDictionary<string, Professor> professorMap)
    {
        var starts = PotentialBlockStarts(course, block);
        var courseProfessors = DomainHelpers.CourseProfIds(course)
            .Select(id => professorMap.TryGetValue(id, out var professor) ? professor : null)
            .Where(professor => professor != null)
            .Cast<Professor>()
            .ToList();
        if (courseProfessors.Count == 0)
            return starts;

        starts.RemoveWhere(start =>
        {
            var periods = Enumerable.Range(start.StartPeriod, block);
            return periods.Any(period =>
                courseProfessors.Any(professor =>
                    professor.UnavailableSlots.Any(slot => slot.Day == start.Day && slot.Period == period)));
        });
        return starts;
    }

    private static bool HasAdjacentStartPair(
        IReadOnlySet<(int Day, int StartPeriod)> firstStarts,
        IReadOnlySet<(int Day, int StartPeriod)> secondStarts,
        int block)
    {
        return firstStarts.Any(start =>
            secondStarts.Contains((start.Day, start.StartPeriod + block)) ||
            secondStarts.Contains((start.Day, start.StartPeriod - block)));
    }

    private static bool IsRequiredMajor(Course course) =>
        course.CourseType == RequiredCourseType;

    private static HashSet<TimeSlot> SlotSet(IEnumerable<TimeSlot> slots) =>
        slots.ToHashSet();

    private static string InputCourseLocation(Course course) =>
        $"교과목 관리 > {CourseLabel(course)}";

    private static string InputCoursePair(Course first, Course second) =>
        $"교과목 관리 > {CourseLabel(first)} / {CourseLabel(second)}";

    private static string InputProfessorLocation(Professor professor) =>
        $"교수 관리 > {ProfessorLabel(professor)}";

    private static string InputProfessorLocations(IEnumerable<string> professorIds) =>
        string.Join(", ", professorIds.Select(id => $"교수 관리 > {id}"));

    private static string InputRoomLocation(Room room) =>
        $"강의실 관리 > {RoomLabel(room)}";

    private static string InputCrossLocation(CrossGroup cross) =>
        $"Cross 설정 > {cross.Id} ({string.Join(", ", cross.BaseIds)})";

    private static string SlotLabels(IEnumerable<TimeSlot> slots) =>
        string.Join(", ", slots
            .Distinct()
            .OrderBy(slot => slot.Day)
            .ThenBy(slot => slot.Period)
            .Select(slot => $"{DayLabel(slot.Day)} {slot.Period}교시"));

    private static string DayLabel(int day) => day switch
    {
        0 => "월",
        1 => "화",
        2 => "수",
        3 => "목",
        4 => "금",
        _ => $"요일{day}",
    };

    private static string CourseLabel(Course course) =>
        string.IsNullOrWhiteSpace(course.Name) ? course.Id : $"{course.Name}({course.Id})";

    private static string ProfessorLabel(Professor professor) =>
        string.IsNullOrWhiteSpace(professor.Name) ? professor.Id : $"{professor.Name}({professor.Id})";

    private static string RoomLabel(Room room) =>
        string.IsNullOrWhiteSpace(room.Name) ? room.Id : $"{room.Name}({room.Id})";

    private static void Add(List<TimetableDiagnostic> issues, string id, string message) =>
        issues.Add(new TimetableDiagnostic(id, message));

    private static IReadOnlyList<TimetableDiagnostic> Distinct(IEnumerable<TimetableDiagnostic> issues) =>
        issues
            .GroupBy(issue => (issue.Id, issue.Message))
            .Select(group => group.First())
            .OrderBy(issue => issue.Id, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToList();
}
