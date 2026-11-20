using System.Text;
using System.Text.Json;
using Audit.Worker.Data;
using Audit.Worker.Entities;
using Auction.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Audit.Worker.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "auction";
    public string Password { get; set; } = "auction123";
    public string VirtualHost { get; set; } = "/";
}

/// <summary>
/// Lắng nghe queue bền vững "audit.auction-events" (bind vào exchange Topic "auction.events")
/// và ghi AuditLogs + BidHistories. Xử lý idempotent theo EventId, ack thủ công.
/// </summary>
public sealed class AuditEventConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<AuditEventConsumer> logger) : BackgroundService
{
    private IConnection? _connection;
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var factory = new ConnectionFactory
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
            ClientProvidedName = "audit-worker"
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection ??= factory.CreateConnection();
                _channel = _connection.CreateModel();

                _channel.ExchangeDeclare(EventTopology.Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
                _channel.QueueDeclare(EventTopology.AuditQueue, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueBind(EventTopology.AuditQueue, EventTopology.Exchange, EventTopology.AllEventsBindingKey);
                _channel.BasicQos(0, 20, false);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += (_, ea) => HandleAsync(ea, stoppingToken);
                _channel.BasicConsume(EventTopology.AuditQueue, autoAck: false, consumer);

                logger.LogInformation("Audit worker listening on queue {Queue} ({Host}:{Port})",
                    EventTopology.AuditQueue, o.HostName, o.Port);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (BrokerUnreachableException ex)
            {
                logger.LogWarning("RabbitMQ not reachable: {Message}. Retrying in 3s...", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Audit consumer failed. Restarting in 5s...");
                _channel?.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var channel = _channel!;
        var type = ea.BasicProperties?.Type ?? ea.RoutingKey;
        var json = Encoding.UTF8.GetString(ea.Body.Span);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var now = DateTime.UtcNow;

            switch (type)
            {
                case nameof(BidAcceptedEvent):
                case EventTopology.BidAcceptedRoutingKey:
                {
                    var e = EventSerializer.Deserialize<BidAcceptedEvent>(ea.Body.Span)
                            ?? throw new InvalidDataException("Empty BidAcceptedEvent");
                    if (await db.AuditLogs.AnyAsync(x => x.EventId == e.EventId, ct))
                    {
                        logger.LogInformation("Duplicate event {EventId} skipped", e.EventId);
                        break;
                    }

                    db.AuditLogs.Add(NewLog(e.EventId, e.AuctionId, "BidAccepted", json, e.OccurredAt, now));
                    if (!await db.BidHistories.AnyAsync(b => b.Id == e.BidId, ct))
                    {
                        db.BidHistories.Add(new BidHistory
                        {
                            Id = e.BidId,
                            AuctionId = e.AuctionId,
                            ProductName = e.ProductName,
                            BidderName = e.BidderName,
                            Amount = e.Amount,
                            PreviousPrice = e.PreviousPrice,
                            BidAt = e.OccurredAt,
                            RecordedAt = now
                        });
                    }
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("[AUDIT] BidAccepted: auction {AuctionId} - {Bidder} bid {Amount}",
                        e.AuctionId, e.BidderName, e.Amount);
                    break;
                }
                case nameof(AuctionEndedEvent):
                case EventTopology.AuctionEndedRoutingKey:
                {
                    var e = EventSerializer.Deserialize<AuctionEndedEvent>(ea.Body.Span)
                            ?? throw new InvalidDataException("Empty AuctionEndedEvent");
                    if (await db.AuditLogs.AnyAsync(x => x.EventId == e.EventId, ct))
                    {
                        logger.LogInformation("Duplicate event {EventId} skipped", e.EventId);
                        break;
                    }

                    db.AuditLogs.Add(NewLog(e.EventId, e.AuctionId, "AuctionEnded", json, e.OccurredAt, now));
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("[AUDIT] AuctionEnded: auction {AuctionId} - winner {Winner} @ {Price} ({EndedBy})",
                        e.AuctionId, e.WinnerId ?? "(none)", e.FinalPrice, e.EndedBy);
                    break;
                }
                default:
                    logger.LogWarning("Unknown event type '{Type}' - message discarded", type);
                    break;
            }

            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            logger.LogError(ex, "Malformed message discarded: {Json}", json);
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Event already recorded (unique violation) - ack");
            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process audit event - requeue");
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private static AuditLog NewLog(Guid eventId, Guid auctionId, string eventType, string payload, DateTime occurredAt, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        EventId = eventId,
        AuctionId = auctionId,
        EventType = eventType,
        Payload = payload,
        OccurredAt = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc),
        CreatedAt = now
    };

    public override void Dispose()
    {
        try { _channel?.Close(); _connection?.Close(); } catch { /* ignore */ }
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
