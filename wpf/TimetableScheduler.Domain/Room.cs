namespace TimetableScheduler.Domain;

public class Room
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsLab { get; set; }
    public int Capacity { get; set; }
}
