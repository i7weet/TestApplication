using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TestApp;

public class RabbitMqPublisher : ITaskEventPublisher, IHostedService
{
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private IChannel? _channel;
    private IConnection? _connection;
    private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
    private readonly ConnectionFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private bool _stopped;
    

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _logger = logger;
        _rabbitMqOptions = options.Value;

        _factory = new ConnectionFactory()
        {
            HostName = _rabbitMqOptions.HostName,
            Port = _rabbitMqOptions.Port,
            UserName = _rabbitMqOptions.UserName,
            Password = _rabbitMqOptions.Password,
            AutomaticRecoveryEnabled = false
        };
    }
    
    public async Task PublishAsync(TaskCompletedEvent taskCompletedEvent, CancellationToken cancellationToken)
    {
        
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
        var token = timeoutCts.Token;
        var semaphoreTaken = false;

        try
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(taskCompletedEvent, JsonOptions);

            await _semaphoreSlim.WaitAsync(token);
            semaphoreTaken = true;
            if (_stopped)
                throw new InvalidOperationException("RabbitMQ publisher is stopped.");
            await EnsureConnectedAsync(token);
            var channel = _channel!;
            await channel.ExchangeDeclareAsync(
                exchange: _rabbitMqOptions.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: token);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                Persistent = true
            };

            // HACK: Publisher confirms отключены.
            // Завершение отправки не гарантирует подтверждённое получение брокером.
            await channel.BasicPublishAsync(
                exchange: _rabbitMqOptions.ExchangeName,
                routingKey: _rabbitMqOptions.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: token);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to publish event: {Event}", taskCompletedEvent);
        }
        finally
        {
            if (semaphoreTaken)
                _semaphoreSlim.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            return;

        // HACK: При закрытом канале пересоздаём и соединение.
        // Для тестового это упрощает восстановление и очистку ресурсов.
        await CloseResourcesAsync(cancellationToken);

        _connection = await _factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            cancellationToken: cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        var semaphoreTaken = false;

        try
        {
            await _semaphoreSlim.WaitAsync(timeoutCts.Token);
            semaphoreTaken = true;
            _stopped = true;

            await CloseResourcesAsync(timeoutCts.Token);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RabbitMQ shutdown failed or timed out.");
        }
        finally
        {
            if (semaphoreTaken)
                _semaphoreSlim.Release();
        }
    }

    private async Task CloseResourcesAsync(CancellationToken cancellationToken)
    {
        var channel = _channel;
        var connection = _connection;
        _channel = null;
        _connection = null;

        try
        {
            if (connection != null)
                if (connection.IsOpen)
                    await connection.CloseAsync(cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to close RabbitMQ connection.");
        }

        foreach (var resource in new IAsyncDisposable?[] { channel, connection })
        {
            if (resource is null)
                continue;

            try
            {
                await resource.DisposeAsync().AsTask().WaitAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to dispose RabbitMQ resource.");
            }
        }
    }
}