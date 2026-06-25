using TimetableScheduler.Domain;

namespace TimetableScheduler.Tests.Domain;

public class ModelDefaultsTests
{
    [Fact]
    public void Course_DefaultsMatchPython()
    {
        var c = new Course();
        Assert.Equal("", c.Id);
        Assert.Equal("", c.Name);
        Assert.Equal(0, c.Grade);
        Assert.Equal(0, c.HoursPerWeek);
        Assert.Equal("전필", c.CourseType);
        Assert.Equal("", c.ProfessorId);
        Assert.Equal(1, c.Section);
        Assert.Equal("", c.Department);
        Assert.Empty(c.FixedRooms);
        Assert.Empty(c.BlockStructure);
        Assert.False(c.IsFixed);
        Assert.Empty(c.FixedSlots);
        Assert.Empty(c.CoteachProfs);
    }

    [Fact]
    public void Professor_DefaultsMatchPython()
    {
        var p = new Professor();
        Assert.Equal("", p.Id);
        Assert.Equal("", p.Name);
        Assert.Empty(p.UnavailableSlots);
        Assert.Empty(p.UnavailableRooms);
    }

    [Fact]
    public void Room_DefaultsMatchPython()
    {
        var r = new Room();
        Assert.Equal("", r.Id);
        Assert.Equal("", r.Name);
        Assert.False(r.IsLab);
        Assert.Equal(0, r.Capacity);
    }

    [Fact]
    public void TimeSlot_ValueEquality()
    {
        Assert.Equal(new TimeSlot(0, 1), new TimeSlot(0, 1));
        Assert.NotEqual(new TimeSlot(0, 1), new TimeSlot(0, 2));
    }

}
