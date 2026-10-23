using Microsoft.EntityFrameworkCore;
using TestApp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TestDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddSingleton<ITaskEventPublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RabbitMqPublisher>());

var app = builder.Build();

app.MapPost("/tasks", async Task<IResult> (CreateTaskRequest request, TestDbContext db, CancellationToken cancellationToken) =>
{
    if(string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        return Results.BadRequest("Incorrect title");
    if (!Enum.IsDefined(request.Priority))
        return Results.BadRequest("Incorrect priority");

    var task = new TaskItem()
    {
        Title = request.Title,
        Priority = request.Priority,
    };

    db.TaskItems.Add(task);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Json(task, statusCode: StatusCodes.Status201Created);
});

app.MapGet("/tasks", async Task<IResult> (TestDbContext dbContext, CancellationToken cancellationToken) =>
{
    var tasks = await dbContext.TaskItems.AsNoTracking().ToListAsync(cancellationToken);
    return Results.Ok(tasks);
});

app.MapDelete("/tasks/{id}", async Task<IResult> (TestDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
{
    var task = await dbContext.TaskItems.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (task == null)
        return Results.NotFound();
    
    dbContext.TaskItems.Remove(task);
    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict();
    }
});

app.MapPut("/tasks/{id}/complete", async Task<IResult> (TestDbContext dbContext, ITaskEventPublisher publisher, Guid id, CancellationToken cancellationToken) =>
{
    var task = await dbContext.TaskItems
        .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    if (task is null)
        return Results.NotFound();

    if (task.IsCompleted)
        return Results.Conflict();

    task.IsCompleted = true;
    task.CompletedAt = DateTimeOffset.UtcNow;

    try
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict();
    }

    var taskCompletedEvent = new TaskCompletedEvent
    {
        TaskId = task.Id,
        Title = task.Title,
        CompletedAt = task.CompletedAt.Value,
        Priority = task.Priority
    };

    // HACK: Нет outbox и повторной доставки.
    // При ошибке публикации событие только логируется в publisher.
    await publisher.PublishAsync(taskCompletedEvent, cancellationToken);

    return Results.Ok();
});

app.Run();

public partial  class Program{}