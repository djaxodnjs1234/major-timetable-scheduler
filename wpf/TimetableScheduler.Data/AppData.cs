using TimetableScheduler.Domain;

namespace TimetableScheduler.Data;

public sealed record AppData(
    List<Course> Courses,
    List<Professor> Professors,
    List<Room> Rooms,
    List<CrossGroup> CrossGroups,
    List<RetakeScenario> RetakeScenarios)
{
    public SchedulePolicy SchedulePolicy { get; init; } = SchedulePolicy.Default;

    public static AppData Empty() => new(
        new List<Course>(),
        new List<Professor>(),
        new List<Room>(),
        new List<CrossGroup>(),
        new List<RetakeScenario>());
}
