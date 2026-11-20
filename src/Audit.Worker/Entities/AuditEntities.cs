namespace Audit.Worker.Entities;

/// <summary>Log kiểm toán mọi sự kiện (bảng "AuditLogs").</summary>
public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>EventId của message - unique để xử lý idempotent (RabbitMQ đảm bảo at-least-once).</summary>
    public Guid EventId { get; set; }

    public Guid AuctionId { get; set; }

    /// <summary>"BidAccepted" | "AuctionEnded"</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Nội dung JSON gốc của sự kiện (jsonb).</summary>
    public string Payload { get; set; } = "{}";

    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Lịch sử trả giá phục vụ kiểm toán (bảng "BidHistories").</summary>
public class BidHistory
{
    /// <summary>Trùng với BidId bên Auction.API.</summary>
    public Guid Id { get; set; }
    public Guid AuctionId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string BidderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PreviousPrice { get; set; }
    public DateTime BidAt { get; set; }
    public DateTime RecordedAt { get; set; }
}
