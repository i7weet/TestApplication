namespace TestApp;

public record TaskCompletedEvent
{
    public Guid TaskId { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public Priority Priority { get; init; }
}