namespace TestApp;

public class CreateTaskRequest
{
    public string? Title { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
}