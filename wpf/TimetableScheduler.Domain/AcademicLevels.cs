using System.Globalization;

namespace TimetableScheduler.Domain;

public sealed record AcademicLevel(int Grade, string DisplayName);

public static class AcademicLevels
{
    public const int GraduateGrade = 5;

    private static readonly int[] UndergraduateGradeValues = { 1, 2, 3, 4 };
    private static readonly int[] AllGradeValues = { 1, 2, 3, 4, GraduateGrade };

    public static IReadOnlyList<int> UndergraduateGrades { get; } =
        Array.AsReadOnly(UndergraduateGradeValues);

    public static IReadOnlyList<int> AllGrades { get; } =
        Array.AsReadOnly(AllGradeValues);

    public static IReadOnlyList<AcademicLevel> All { get; } =
        Array.AsReadOnly(AllGradeValues.Select(grade => new AcademicLevel(grade, DisplayName(grade))).ToArray());

    public static string DisplayName(int grade) =>
        grade == GraduateGrade ? "대학원" : grade > 0 ? $"{grade}학년" : "";

    public static bool TryParse(string? value, out int grade)
    {
        grade = 0;
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var compact = string.Concat(text.Where(ch => !char.IsWhiteSpace(ch)));
        if (TryParseNumber(compact, out grade))
            return true;

        if (compact.EndsWith("학년", StringComparison.Ordinal) &&
            TryParseNumber(compact[..^2], out grade))
            return true;

        var lowered = compact.ToLowerInvariant();
        if (lowered is "대학원" or "대학원생" or "석사" or "박사" or "석박사" or "graduate" or "grad")
        {
            grade = GraduateGrade;
            return true;
        }

        return false;
    }

    private static bool TryParseNumber(string text, out int grade)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out grade) ||
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out grade))
        {
            return grade > 0;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var current) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out current))
        {
            grade = (int)current;
            return grade > 0;
        }

        grade = 0;
        return false;
    }
}
