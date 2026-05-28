namespace TimetableScheduler.Domain;

public class CrossGroup
{
    public string Id { get; set; } = "";

    public List<string> BaseIds { get; set; } = new();
}
