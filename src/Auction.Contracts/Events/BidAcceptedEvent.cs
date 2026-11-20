namespace Auction.Contracts.Events;

/// <summary>Phát ra khi một lượt trả giá được chấp nhận và đã lưu DB.</summary>
public sealed record BidAcceptedEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    public Guid AuctionId { get; init; }
    public Guid BidId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string BidderName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal PreviousPrice { get; init; }
    public decimal MinNextBid { get; init; }
    public int TotalBids { get; init; }
}
