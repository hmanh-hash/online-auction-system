using System.Globalization;
using Auction.Contracts.Protos;

namespace BidValidation.Service.Validation;

public sealed record BidValidationOutcome(bool IsValid, string ErrorCode, string Reason, decimal MinAcceptableAmount);

/// <summary>
/// Luật nghiệp vụ kiểm tra một lượt trả giá (pure function - dễ unit test).
/// </summary>
public static class BidRules
{
    public const int MaxBidderNameLength = 50;
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static BidValidationOutcome Validate(ValidateBidRequest r, DateTime nowUtc)
    {
        decimal starting = r.StartingPrice;
        decimal current = r.CurrentPrice;
        decimal step = r.MinStepPrice;
        decimal amount = r.Amount;

        // Lượt đầu tiên: >= giá khởi điểm. Các lượt sau: >= giá hiện tại + bước giá.
        var min = r.HasBids ? current + step : starting;

        BidValidationOutcome Fail(string code, string reason) => new(false, code, reason, min);
        string M(decimal v) => v.ToString("#,##0.##", Vi);

        if (!Guid.TryParse(r.AuctionId, out _))
            return Fail("INVALID_AUCTION", "Mã phiên đấu giá không hợp lệ.");

        // 1. Người trả giá có hợp lệ không
        var bidder = r.BidderName?.Trim() ?? string.Empty;
        if (bidder.Length == 0)
            return Fail("INVALID_BIDDER", "Tên người trả giá không được để trống.");
        if (bidder.Length > MaxBidderNameLength)
            return Fail("INVALID_BIDDER", $"Tên người trả giá tối đa {MaxBidderNameLength} ký tự.");

        // 2. Phiên có đang mở không + thời gian có nằm trong [StartTime, EndTime) không
        if (r.Status == AuctionStatus.Ended)
            return Fail("AUCTION_ENDED", "Phiên đấu giá đã kết thúc.");
        if (r.StartTime is null || r.EndTime is null)
            return Fail("INVALID_AUCTION", "Phiên đấu giá thiếu thông tin thời gian.");

        var start = r.StartTime.ToDateTime();
        var end = r.EndTime.ToDateTime();
        if (nowUtc < start)
            return Fail("AUCTION_NOT_STARTED", $"Phiên đấu giá chưa bắt đầu (bắt đầu lúc {start:yyyy-MM-dd HH:mm:ss} UTC).");
        if (nowUtc >= end)
            return Fail("AUCTION_EXPIRED", "Phiên đấu giá đã hết thời gian.");
        if (r.Status != AuctionStatus.Active)
            return Fail("AUCTION_NOT_ACTIVE", "Phiên đấu giá chưa được mở.");

        // 3. Giá trị lượt trả giá
        if (amount <= 0)
            return Fail("INVALID_AMOUNT", "Giá đặt phải lớn hơn 0.");
        if (decimal.Round(amount, 2) != amount)
            return Fail("INVALID_AMOUNT", "Giá đặt chỉ được tối đa 2 chữ số thập phân.");

        // 4. Không cho phép người đang dẫn đầu tự nâng giá của chính mình
        if (r.HasBids && string.Equals(bidder, r.CurrentHighestBidder?.Trim(), StringComparison.OrdinalIgnoreCase))
            return Fail("ALREADY_HIGHEST_BIDDER", "Bạn đang là người trả giá cao nhất, không thể tự nâng giá của chính mình.");

        if (amount < min)
        {
            return Fail("AMOUNT_TOO_LOW", r.HasBids
                ? $"Giá đặt phải ≥ giá hiện tại ({M(current)}) + bước giá ({M(step)}) = {M(min)}."
                : $"Lượt trả giá đầu tiên phải ≥ giá khởi điểm {M(min)}.");
        }

        return new BidValidationOutcome(true, string.Empty, "Hợp lệ", min);
    }
}
