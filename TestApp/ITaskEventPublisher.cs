namespace TestApp;

public interface ITaskEventPublisher
{
    public Task PublishAsync(TaskCompletedEvent taskCompletedEvent, CancellationToken cancellationToken);
}