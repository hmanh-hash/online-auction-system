namespace Auction.Contracts.Events;

/// <summary>Phát ra khi phiên đấu giá kết thúc (thủ công hoặc do scheduler hết giờ).</summary>
public sealed record AuctionEndedEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    public Guid AuctionId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? WinnerId { get; init; }
    public decimal FinalPrice { get; init; }
    public int TotalBids { get; init; }
    public DateTime EndedAt { get; init; }

    /// <summary>"Manual" (gọi API) hoặc "Scheduler" (tự động hết giờ).</summary>
    public string EndedBy { get; init; } = "Manual";
}
