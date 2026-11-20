namespace Auction.API.Entities;

public enum AuctionStatus
{
    Pending = 0,
    Active = 1,
    Ended = 2
}

/// <summary>Phiên đấu giá (bảng "Auctions").</summary>
public class AuctionItem
{
    public Guid Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal StartingPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal MinStepPrice { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public AuctionStatus Status { get; set; }
    public string? WinnerId { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Concurrency token: ánh xạ tới cột hệ thống "xmin" của PostgreSQL (Optimistic Concurrency).
    /// Mọi UPDATE đều kèm điều kiện WHERE xmin = @original -> phát hiện race condition.
    /// </summary>
    public uint Version { get; set; }

    public List<Bid> Bids { get; set; } = new();
}

/// <summary>Lượt trả giá (bảng "Bids").</summary>
public class Bid
{
    public Guid Id { get; set; }
    public Guid AuctionId { get; set; }
    public string BidderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }

    public AuctionItem? Auction { get; set; }
}
