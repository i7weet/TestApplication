namespace TestApp;

public class TaskItem
{
    public Guid Id { get; set; } =  Guid.NewGuid();
    public required string Title { get; set; } // не null, не пустой, максимум 200
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; } // когда завершили
    public Priority Priority { get; set; } = Priority.Medium; // Low, Medium, High
    public uint RowVersion { get; set; }
}