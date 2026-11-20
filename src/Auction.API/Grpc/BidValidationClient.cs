using Auction.API.Entities;
using Auction.Contracts.Protos;
using Google.Protobuf.WellKnownTypes;
using ProtoStatus = Auction.Contracts.Protos.AuctionStatus;

namespace Auction.API.Grpc;

public sealed record BidValidationResult(bool IsValid, string ErrorCode, string Reason, decimal MinAcceptableAmount);

/// <summary>Wrapper quanh gRPC client sinh tự động từ bid_validation.proto.</summary>
public sealed class BidValidationClient(BidValidator.BidValidatorClient client)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    public async Task<BidValidationResult> ValidateAsync(
        AuctionItem auction, string? currentHighestBidder, string? bidderName, decimal amount, CancellationToken ct)
    {
        var request = new ValidateBidRequest
        {
            AuctionId = auction.Id.ToString(),
            Status = auction.Status switch
            {
                Entities.AuctionStatus.Active => ProtoStatus.Active,
                Entities.AuctionStatus.Ended => ProtoStatus.Ended,
                _ => ProtoStatus.Pending
            },
            StartTime = Timestamp.FromDateTime(DateTime.SpecifyKind(auction.StartTime, DateTimeKind.Utc)),
            EndTime = Timestamp.FromDateTime(DateTime.SpecifyKind(auction.EndTime, DateTimeKind.Utc)),
            StartingPrice = auction.StartingPrice,
            CurrentPrice = auction.CurrentPrice,
            MinStepPrice = auction.MinStepPrice,
            HasBids = currentHighestBidder is not null,
            CurrentHighestBidder = currentHighestBidder ?? string.Empty,
            BidderName = bidderName ?? string.Empty,
            Amount = amount
        };

        var response = await client.ValidateBidAsync(request, deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: ct);
        return new BidValidationResult(response.IsValid, response.ErrorCode, response.Reason, response.MinAcceptableAmount);
    }
}
