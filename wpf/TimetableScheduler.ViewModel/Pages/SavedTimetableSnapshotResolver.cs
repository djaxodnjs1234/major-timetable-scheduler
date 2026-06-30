using System.Text.Json;
using TimetableScheduler.Data;
using TimetableScheduler.Domain;

namespace TimetableScheduler.ViewModel.Pages;

internal static class SavedTimetableSnapshotResolver
{
    public static AppData Resolve(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return AppData.Empty();

        try
        {
            var snapshot = JsonSerializer.Deserialize<AppData>(snapshotJson);
            return IsValid(snapshot) ? snapshot! : AppData.Empty();
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
        || (!string.IsNullOrWhiteSpace(course.ProfessorId)
            && !string.IsNullOrWhiteSpace(course.CourseType));
}
