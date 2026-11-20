using Auction.API.Hubs;
using Auction.Contracts.Events;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Auction.API.Messaging;

/// <summary>
/// Consumer RabbitMQ chạy trong Auction.API: nhận BidAccepted / AuctionEnded rồi đẩy realtime
/// qua SignalR tới group của phiên đấu giá. Mỗi instance API có một queue exclusive riêng
/// (server-named, auto-delete) bind vào exchange Topic => mọi instance đều nhận đủ sự kiện.
/// </summary>
public sealed class AuctionEventsSignalRConsumer(
    RabbitMqConnectionProvider connectionProvider,
    IHubContext<AuctionHub> hub,
    ILogger<AuctionEventsSignalRConsumer> logger) : BackgroundService
{
    private IModel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await connectionProvider.GetConnectionAsync(stoppingToken);
                _channel = connection.CreateModel();
                RabbitMqTopology.Declare(_channel);

                var queueName = _channel.QueueDeclare(queue: string.Empty, durable: false, exclusive: true, autoDelete: true).QueueName;
                _channel.QueueBind(queueName, EventTopology.Exchange, EventTopology.AllEventsBindingKey);
                _channel.BasicQos(0, 50, false);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += (_, ea) => HandleAsync(ea, stoppingToken);
                _channel.BasicConsume(queueName, autoAck: false, consumer);

                logger.LogInformation("SignalR consumer listening on queue {Queue}", queueName);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SignalR consumer failed. Restarting in 5s...");
                _channel?.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var channel = _channel!;
        try
        {
            var type = ea.BasicProperties?.Type ?? ea.RoutingKey;
            switch (type)
            {
                case nameof(BidAcceptedEvent):
                case EventTopology.BidAcceptedRoutingKey:
                {
                    var e = EventSerializer.Deserialize<BidAcceptedEvent>(ea.Body.Span)!;
                    await hub.Clients.Group(AuctionHub.GroupName(e.AuctionId)).SendAsync("BidAccepted", e, ct);
                    await hub.Clients.All.SendAsync("AuctionUpdated", new
                    {
                        id = e.AuctionId,
                        currentPrice = e.Amount,
                        minNextBid = e.MinNextBid,
                        totalBids = e.TotalBids,
                        highestBidder = e.BidderName
                    }, ct);
                    break;
                }
                case nameof(AuctionEndedEvent):
                case EventTopology.AuctionEndedRoutingKey:
                {
                    var e = EventSerializer.Deserialize<AuctionEndedEvent>(ea.Body.Span)!;
                    await hub.Clients.Group(AuctionHub.GroupName(e.AuctionId)).SendAsync("AuctionEnded", e, ct);
                    await hub.Clients.All.SendAsync("AuctionUpdated", new
                    {
                        id = e.AuctionId,
                        status = "Ended",
                        winnerId = e.WinnerId,
                        currentPrice = e.FinalPrice,
                        totalBids = e.TotalBids,
                        endTime = e.EndedAt
                    }, ct);
                    break;
                }
                default:
                    logger.LogWarning("Unknown event type {Type}", type);
                    break;
            }

            channel.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to push event to SignalR clients");
            channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override void Dispose()
    {
        try { _channel?.Close(); } catch { /* ignore */ }
        _channel?.Dispose();
        base.Dispose();
    }
}
