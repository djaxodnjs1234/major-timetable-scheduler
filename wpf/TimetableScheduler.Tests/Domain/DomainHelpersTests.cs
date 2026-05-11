using TimetableScheduler.Domain;

namespace TimetableScheduler.Tests.Domain;

public class DomainHelpersTests
{
    [Theory]
    [InlineData("GA1004-01", "GA1004")]
    [InlineData("GA1004", "GA1004")]
    [InlineData("AB-CD-EF", "AB-CD")]
    [InlineData("X-1", "X")]
    [InlineData("-leading", "")]
    public void BaseId_StripsLastDashSegment(string input, string expected)
    {
        Assert.Equal(expected, DomainHelpers.BaseId(input));
    }

    [Fact]
    public void DeriveAutoRetakes_OnlyMajorsCounted()
    {
        var courses = new List<Course>
        {
            new() { Id = "M1-01", Grade = 1, CourseType = "전필" },
            new() { Id = "M2-01", Grade = 2, CourseType = "전필" },
            new() { Id = "E1-01", Grade = 1, CourseType = "전선" },
            new() { Id = "G1-01", Grade = 1, CourseType = "교양" },
        };

        var retakes = DomainHelpers.DeriveAutoRetakes(courses);

        var pairs = retakes
            .Select(r => (r.CurrentGrade, r.RetakeBaseId))
            .ToHashSet();

        var expected = new HashSet<(int, string)>
        {
            (2, "M1"), (3, "M1"), (4, "M1"),
            (3, "M2"), (4, "M2"),
        };
        Assert.Equal(expected, pairs);
    }

    [Fact]
    public void DeriveAutoRetakes_MultipleSectionsCollapseToBase()
    {
        var courses = new List<Course>
        {
            new() { Id = "M1-01", Grade = 1, CourseType = "전필" },
            new() { Id = "M1-02", Grade = 1, CourseType = "전필" },
            new() { Id = "M1-03", Grade = 1, CourseType = "전필" },
        };

        var retakes = DomainHelpers.DeriveAutoRetakes(courses);

        Assert.Equal(3, retakes.Count);
        Assert.All(retakes, r => Assert.Equal("M1", r.RetakeBaseId));
    }

    [Fact]
    public void DeriveAutoRetakes_CustomGradeRange()
    {
        var courses = new List<Course>
        {
            new() { Id = "M1-01", Grade = 1, CourseType = "전필" },
        };

        var retakes = DomainHelpers.DeriveAutoRetakes(courses, new[] { 1, 2 });

        Assert.Single(retakes);
        Assert.Equal(2, retakes[0].CurrentGrade);
    }
}
