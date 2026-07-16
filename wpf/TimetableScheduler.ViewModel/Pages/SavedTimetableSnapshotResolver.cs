using System.Text.Json;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.ViewModel.Pages;

internal static class SavedTimetableSnapshotResolver
{
    private const string ManualBlockCourseIdPrefix = "manual-block-";
    private static readonly string ManualBlockDefaultCourseType = new Course().CourseType;

    public static AppData Resolve(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return AppData.Empty();

        try
        {
            var snapshot = JsonSerializer.Deserialize<AppData>(snapshotJson);
            if (!IsValid(snapshot))
                return AppData.Empty();

            RepairLegacyManualBlockCourses(snapshot!);
            return snapshot!;
        }
        catch (JsonException)
        {
            return AppData.Empty();
        }
    }

    private static bool IsValid(AppData? snapshot)
    {
        if (snapshot == null)
            return false;

        return snapshot.Courses.All(IsValidCourse);
    }

    private static bool IsValidCourse(Course course) =>
        course.IsSchoolFixed
        || IsManualBlockCourse(course)
        || (!string.IsNullOrWhiteSpace(course.ProfessorId)
            && !string.IsNullOrWhiteSpace(course.CourseType));

    private static bool IsManualBlockCourse(Course course) =>
        course.Id.StartsWith(ManualBlockCourseIdPrefix, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(course.ProfessorId);

    private static void RepairLegacyManualBlockCourses(AppData snapshot)
    {
        foreach (var course in snapshot.Courses.Where(IsManualBlockCourse))
        {
            if (string.IsNullOrWhiteSpace(course.CourseType))
                course.CourseType = ManualBlockDefaultCourseType;
        }
    }
}
