using Auction.Contracts.Events;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Auction.API.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "auction";
    public string Password { get; set; } = "auction123";
    public string VirtualHost { get; set; } = "/";
}

/// <summary>Quản lý một kết nối RabbitMQ dùng chung (thread-safe, tự retry, auto-recovery).</summary>
public sealed class RabbitMqConnectionProvider : IDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _logger = logger;
        var o = options.Value;
        _factory = new ConnectionFactory
        {
            HostName = o.HostName,
            Port = o.Port,
            UserName = o.UserName,
            Password = o.Password,
            VirtualHost = o.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            ClientProvidedName = "auction-api"
        };
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is not null) return _connection;

        await _gate.WaitAsync(ct);
        try
        {
            var attempt = 0;
            while (_connection is null)
            {
                try
                {
                    _connection = _factory.CreateConnection();
                    _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", _factory.HostName, _factory.Port);
                }
                catch (BrokerUnreachableException ex)
                {
                    attempt++;
                    _logger.LogWarning("RabbitMQ not reachable (attempt {Attempt}): {Message}. Retrying in 3s...", attempt, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
            }
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        try { _connection?.Close(); } catch { /* ignore */ }
        _connection?.Dispose();
        _gate.Dispose();
    }
}

public static class RabbitMqTopology
{
    /// <summary>
    /// Khai báo exchange Topic và queue bền vững của Audit (idempotent).
    /// Khai báo queue Audit ngay từ phía publisher đảm bảo không mất sự kiện kể cả khi Audit.Worker chưa chạy.
    /// </summary>
    public static void Declare(IModel channel)
    {
        channel.ExchangeDeclare(EventTopology.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.QueueDeclare(EventTopology.AuditQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(EventTopology.AuditQueue, EventTopology.Exchange, EventTopology.AllEventsBindingKey);
    }
}
