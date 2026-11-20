using System.ComponentModel.DataAnnotations;
using Auction.API.Entities;

namespace Auction.API.Models;

public sealed class CreateAuctionRequest : IValidatableObject
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string ProductName { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    public decimal StartingPrice { get; set; }
    public decimal MinStepPrice { get; set; }

    [Required]
    public DateTime? StartTime { get; set; }

    [Required]
    public DateTime? EndTime { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartingPrice <= 0)
            yield return new ValidationResult("Giá khởi điểm phải lớn hơn 0.", [nameof(StartingPrice)]);
        if (MinStepPrice <= 0)
            yield return new ValidationResult("Bước giá phải lớn hơn 0.", [nameof(MinStepPrice)]);

        if (StartTime is null || EndTime is null) yield break;

        var start = StartTime.Value.ToUtc();
        var end = EndTime.Value.ToUtc();
        if (end <= start)
            yield return new ValidationResult("Thời gian kết thúc phải sau thời gian bắt đầu.", [nameof(EndTime)]);
        if (end <= DateTime.UtcNow)
            yield return new ValidationResult("Thời gian kết thúc phải ở tương lai.", [nameof(EndTime)]);
    }
}

public sealed class PlaceBidRequest
{
    /// <summary>Không đặt [Required] để phần kiểm tra nghiệp vụ do BidValidation.Service (gRPC) đảm nhận.</summary>
    public string? BidderName { get; set; }

    [Required]
    public decimal? Amount { get; set; }
}

public sealed class AuctionDto
{
    public Guid Id { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal StartingPrice { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal MinStepPrice { get; init; }
    public decimal MinNextBid { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public AuctionStatus Status { get; init; }
    public string? WinnerId { get; init; }
    public int TotalBids { get; init; }
    public string? HighestBidder { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record BidDto(Guid Id, Guid AuctionId, string BidderName, decimal Amount, DateTime CreatedAt);

public sealed record PlaceBidResponse(BidDto Bid, decimal CurrentPrice, decimal MinNextBid);

public sealed record ApiError(string Code, string Message, decimal? MinAcceptableAmount = null);

public static class DateTimeExtensions
{
    public static DateTime ToUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
