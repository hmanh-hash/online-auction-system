using Auction.Contracts.Events;
using RabbitMQ.Client;

namespace Auction.API.Messaging;

public interface IEventPublisher
{
    Task PublishAsync<T>(T @event, string routingKey, CancellationToken ct = default) where T : class;
}

/// <summary>Publisher dùng Publisher Confirms để đảm bảo broker đã nhận message.</summary>
public sealed class RabbitMqEventPublisher(RabbitMqConnectionProvider connectionProvider, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1); // IModel không thread-safe
    private IModel? _channel;

    public async Task PublishAsync<T>(T @event, string routingKey, CancellationToken ct = default) where T : class
    {
        var body = EventSerializer.Serialize(@event);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        await _gate.WaitAsync(timeout.Token);
        try
        {
            var channel = await EnsureChannelAsync(timeout.Token);

            var props = channel.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            props.Type = typeof(T).Name;
            props.MessageId = Guid.NewGuid().ToString();
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            channel.BasicPublish(EventTopology.Exchange, routingKey, mandatory: false, props, body);
            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));

            logger.LogInformation("Published {EventType} with routing key {RoutingKey}", typeof(T).Name, routingKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IModel> EnsureChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        _channel?.Dispose();
        var connection = await connectionProvider.GetConnectionAsync(ct);
        var channel = connection.CreateModel();
        channel.ConfirmSelect();
        RabbitMqTopology.Declare(channel);
        _channel = channel;
        return channel;
    }

    public void Dispose()
    {
        try { _channel?.Close(); } catch { /* ignore */ }
        _channel?.Dispose();
        _gate.Dispose();
    }
}
