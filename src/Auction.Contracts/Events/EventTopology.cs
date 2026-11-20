namespace Auction.Contracts.Events;

/// <summary>
/// Cấu hình topology RabbitMQ dùng chung giữa Publisher và các Consumer.
/// Exchange kiểu Topic: một sự kiện được phân phối đồng thời tới
///  - Queue tạm (exclusive) của mỗi instance Auction.API  -> đẩy SignalR
///  - Queue bền vững (durable) của Audit.Worker          -> ghi lịch sử kiểm toán
/// </summary>
public static class EventTopology
{
    public const string Exchange = "auction.events";

    public const string BidAcceptedRoutingKey = "auction.bid.accepted";
    public const string AuctionEndedRoutingKey = "auction.ended";

    /// <summary>Binding key nhận mọi sự kiện của domain đấu giá.</summary>
    public const string AllEventsBindingKey = "auction.#";

    /// <summary>Queue bền vững của Audit.Worker.</summary>
    public const string AuditQueue = "audit.auction-events";
}
