namespace TimetableScheduler.Domain;

public class Professor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    public List<TimeSlot> UnavailableSlots { get; set; } = new();

    public List<string> AllowedRooms { get; set; } = new();
}
