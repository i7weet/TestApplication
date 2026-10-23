using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TestApp.Tests;

public class CompleteTaskIntegrationTest
{
    [Fact]
    public async Task CompleteTask_PublishesEvent()
    {
        var publisher = new FakeTaskEventPublisher();

        // HACK: Используем PostgreSQL из compose.
        // Для изолированного запуска нужна отдельная тестовая БД.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITaskEventPublisher>();
                    services.AddSingleton<ITaskEventPublisher>(publisher);
                });
            });

        using var client = factory.CreateClient();
        using var createResponse = await client.PostAsJsonAsync("/tasks",
            new CreateTaskRequest
            {
                Title = "Integration test",
                Priority = Priority.High
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var task = await createResponse.Content.ReadFromJsonAsync<TaskItem>();
        Assert.NotNull(task);

        try
        {
            Assert.False(task.IsCompleted);
            Assert.Null(task.CompletedAt);

            using var completeResponse = await client.PutAsync($"/tasks/{task.Id}/complete", null);

            Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
            var message = Assert.Single(publisher.Events);
            Assert.Equal(task.Id, message.TaskId);
            Assert.Equal(task.Title, message.Title);
            Assert.Equal(Priority.High, message.Priority);
            Assert.NotEqual(default(DateTimeOffset), message.CompletedAt);
        }
        finally
        {
            using var deleteResponse = await client.DeleteAsync($"/tasks/{task.Id}");
        }
    }

    private sealed class FakeTaskEventPublisher : ITaskEventPublisher
    {
        public List<TaskCompletedEvent> Events { get; } = new();

        public Task PublishAsync(TaskCompletedEvent taskCompletedEvent, CancellationToken cancellationToken)
        {
            Events.Add(taskCompletedEvent);
            return Task.CompletedTask;
        }
    }
}
