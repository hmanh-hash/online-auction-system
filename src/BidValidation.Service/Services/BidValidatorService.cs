using Auction.Contracts.Protos;
using BidValidation.Service.Validation;
using Grpc.Core;

namespace BidValidation.Service.Services;

public sealed class BidValidatorService(ILogger<BidValidatorService> logger) : BidValidator.BidValidatorBase
{
    public override Task<ValidateBidResponse> ValidateBid(ValidateBidRequest request, ServerCallContext context)
    {
        var outcome = BidRules.Validate(request, DateTime.UtcNow);

        if (outcome.IsValid)
        {
            logger.LogInformation("VALID bid {Amount} by '{Bidder}' on auction {AuctionId}",
                (decimal)request.Amount, request.BidderName, request.AuctionId);
        }
        else
        {
            logger.LogInformation("REJECTED bid {Amount} by '{Bidder}' on auction {AuctionId}: {Code} - {Reason}",
                (decimal)request.Amount, request.BidderName, request.AuctionId, outcome.ErrorCode, outcome.Reason);
        }

        return Task.FromResult(new ValidateBidResponse
        {
            IsValid = outcome.IsValid,
            ErrorCode = outcome.ErrorCode,
            Reason = outcome.Reason,
            MinAcceptableAmount = outcome.MinAcceptableAmount
        });
    }
}
