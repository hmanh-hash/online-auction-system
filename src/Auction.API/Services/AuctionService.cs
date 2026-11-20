using Auction.API.Data;
using Auction.API.Entities;
using Auction.API.Grpc;
using Auction.API.Hubs;
using Auction.API.Messaging;
using Auction.API.Models;
using Auction.Contracts.Events;
using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Auction.API.Services;

public sealed class AuctionService(
    AuctionDbContext db,
    BidValidationClient validator,
    IEventPublisher publisher,
    IHubContext<AuctionHub> hub,
    ILogger<AuctionService> logger)
{
    private const int MaxConcurrencyRetries = 5;

    // ----------------------------------------------------------------- Queries

    public async Task<List<AuctionDto>> GetAllAsync(AuctionStatus? status, CancellationToken ct)
    {
        var query = db.Auctions.AsNoTracking();
        if (status is not null) query = query.Where(a => a.Status == status);
        return await Project(query.OrderByDescending(a => a.CreatedAt)).ToListAsync(ct);
    }

    public Task<AuctionDto?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Project(db.Auctions.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(ct);

    public async Task<OperationResult<List<BidDto>>> GetBidsAsync(Guid auctionId, CancellationToken ct)
    {
        if (!await db.Auctions.AnyAsync(a => a.Id == auctionId, ct))
            return OperationResult<List<BidDto>>.NotFound(auctionId);

        var bids = await db.Bids.AsNoTracking()
            .Where(b => b.AuctionId == auctionId)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BidDto(b.Id, b.AuctionId, b.BidderName, b.Amount, b.CreatedAt))
            .ToListAsync(ct);
        return OperationResult<List<BidDto>>.Ok(bids);
    }

    private static IQueryable<AuctionDto> Project(IQueryable<AuctionItem> query) => query.Select(a => new AuctionDto
    {
        Id = a.Id,
        ProductName = a.ProductName,
        Description = a.Description,
        StartingPrice = a.StartingPrice,
        CurrentPrice = a.CurrentPrice,
        MinStepPrice = a.MinStepPrice,
        MinNextBid = a.Bids.Any() ? a.CurrentPrice + a.MinStepPrice : a.StartingPrice,
        StartTime = a.StartTime,
        EndTime = a.EndTime,
        Status = a.Status,
        WinnerId = a.WinnerId,
        TotalBids = a.Bids.Count,
        HighestBidder = a.Bids.OrderByDescending(b => b.Amount).ThenBy(b => b.CreatedAt)
            .Select(b => b.BidderName).FirstOrDefault(),
        CreatedAt = a.CreatedAt
    });

    // ----------------------------------------------------------------- Commands

    public async Task<AuctionDto> CreateAsync(CreateAuctionRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var start = request.StartTime!.Value.ToUtc();
        var auction = new AuctionItem
        {
            Id = Guid.NewGuid(),
            ProductName = request.ProductName.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            StartingPrice = request.StartingPrice,
            CurrentPrice = request.StartingPrice,
            MinStepPrice = request.MinStepPrice,
            StartTime = start,
            EndTime = request.EndTime!.Value.ToUtc(),
            Status = start <= now ? AuctionStatus.Active : AuctionStatus.Pending,
            CreatedAt = now
        };

        db.Auctions.Add(auction);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created auction {AuctionId} '{Product}' ({Status})", auction.Id, auction.ProductName, auction.Status);

        var dto = (await GetByIdAsync(auction.Id, ct))!;
        await hub.Clients.All.SendAsync("AuctionCreated", dto, ct);
        return dto;
    }

    /// <summary>
    /// Đặt giá: gRPC validate -> cập nhật CurrentPrice + insert Bid trong cùng transaction
    /// với Optimistic Concurrency (xmin). Nếu có xung đột, reload & validate lại (tối đa 5 lần).
    /// </summary>
    public async Task<OperationResult<PlaceBidResponse>> PlaceBidAsync(Guid auctionId, PlaceBidRequest request, CancellationToken ct)
    {
        var bidderName = request.BidderName?.Trim();
        var amount = request.Amount ?? 0m;

        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            var auction = await db.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId, ct);
            if (auction is null) return OperationResult<PlaceBidResponse>.NotFound(auctionId);

            // Kích hoạt "lười": phiên Pending đã tới giờ bắt đầu nhưng scheduler chưa quét tới
            var now = DateTime.UtcNow;
            if (auction.Status == AuctionStatus.Pending && auction.StartTime <= now && now < auction.EndTime)
                auction.Status = AuctionStatus.Active;

            var highestBidder = await db.Bids
                .Where(b => b.AuctionId == auctionId)
                .OrderByDescending(b => b.Amount).ThenBy(b => b.CreatedAt)
                .Select(b => b.BidderName)
                .FirstOrDefaultAsync(ct);

            BidValidationResult validation;
            try
            {
                validation = await validator.ValidateAsync(auction, highestBidder, bidderName, amount, ct);
            }
            catch (RpcException ex)
            {
                logger.LogError(ex, "BidValidation.Service unavailable");
                return OperationResult<PlaceBidResponse>.Fail(StatusCodes.Status503ServiceUnavailable,
                    "BID_VALIDATION_UNAVAILABLE", $"Dịch vụ kiểm tra giá không khả dụng: {ex.Status.Detail}");
            }

            if (!validation.IsValid)
            {
                return OperationResult<PlaceBidResponse>.Fail(StatusCodes.Status400BadRequest,
                    validation.ErrorCode, validation.Reason, validation.MinAcceptableAmount);
            }

            var previousPrice = auction.CurrentPrice;
            auction.CurrentPrice = amount;
            var bid = new Bid
            {
                Id = Guid.NewGuid(),
                AuctionId = auctionId,
                BidderName = bidderName!,
                Amount = amount,
                CreatedAt = now
            };
            db.Bids.Add(bid);

            try
            {
                // UPDATE "Auctions" ... WHERE "Id" = @id AND xmin = @version  +  INSERT "Bids" (1 transaction)
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogWarning("Concurrency conflict on auction {AuctionId} (attempt {Attempt}) - retrying", auctionId, attempt);
                db.ChangeTracker.Clear();
                continue;
            }

            var totalBids = await db.Bids.CountAsync(b => b.AuctionId == auctionId, ct);
            var minNextBid = amount + auction.MinStepPrice;

            await PublishSafeAsync(new BidAcceptedEvent
            {
                AuctionId = auctionId,
                BidId = bid.Id,
                ProductName = auction.ProductName,
                BidderName = bid.BidderName,
                Amount = amount,
                PreviousPrice = previousPrice,
                MinNextBid = minNextBid,
                TotalBids = totalBids,
                OccurredAt = now
            }, EventTopology.BidAcceptedRoutingKey, ct);

            var dto = new BidDto(bid.Id, bid.AuctionId, bid.BidderName, bid.Amount, bid.CreatedAt);
            return OperationResult<PlaceBidResponse>.Ok(new PlaceBidResponse(dto, amount, minNextBid), StatusCodes.Status201Created);
        }

        return OperationResult<PlaceBidResponse>.Fail(StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT",
            "Có quá nhiều lượt trả giá đồng thời, vui lòng thử lại.");
    }

    /// <summary>Kết thúc phiên: xác định người trả giá cao nhất làm WinnerId, phát AuctionEnded.</summary>
    public async Task<OperationResult<AuctionDto>> EndAuctionAsync(Guid auctionId, string endedBy, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxConcurrencyRetries; attempt++)
        {
            var auction = await db.Auctions.FirstOrDefaultAsync(a => a.Id == auctionId, ct);
            if (auction is null) return OperationResult<AuctionDto>.NotFound(auctionId);
            if (auction.Status == AuctionStatus.Ended)
                return OperationResult<AuctionDto>.Fail(StatusCodes.Status409Conflict, "AUCTION_ALREADY_ENDED", "Phiên đấu giá đã kết thúc trước đó.");

            var topBid = await db.Bids
                .Where(b => b.AuctionId == auctionId)
                .OrderByDescending(b => b.Amount).ThenBy(b => b.CreatedAt)
                .FirstOrDefaultAsync(ct);
            var totalBids = await db.Bids.CountAsync(b => b.AuctionId == auctionId, ct);

            var now = DateTime.UtcNow;
            auction.Status = AuctionStatus.Ended;
            auction.WinnerId = topBid?.BidderName;
            if (auction.EndTime > now) auction.EndTime = now; // kết thúc sớm (thủ công)

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                logger.LogWarning("Concurrency conflict while ending auction {AuctionId} (attempt {Attempt})", auctionId, attempt);
                db.ChangeTracker.Clear();
                continue;
            }

            logger.LogInformation("Auction {AuctionId} ended by {EndedBy}. Winner: {Winner} @ {Price}",
                auctionId, endedBy, auction.WinnerId ?? "(none)", auction.CurrentPrice);

            await PublishSafeAsync(new AuctionEndedEvent
            {
                AuctionId = auctionId,
                ProductName = auction.ProductName,
                WinnerId = auction.WinnerId,
                FinalPrice = auction.CurrentPrice,
                TotalBids = totalBids,
                EndedAt = auction.EndTime,
                EndedBy = endedBy,
                OccurredAt = now
            }, EventTopology.AuctionEndedRoutingKey, ct);

            return OperationResult<AuctionDto>.Ok((await GetByIdAsync(auctionId, ct))!);
        }

        return OperationResult<AuctionDto>.Fail(StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT",
            "Không thể kết thúc phiên do xung đột đồng thời, vui lòng thử lại.");
    }

    // ----------------------------------------------------------------- Scheduler helpers

    public async Task ActivateDueAuctionsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await db.Auctions
            .Where(a => a.Status == AuctionStatus.Pending && a.StartTime <= now && a.EndTime > now)
            .ToListAsync(ct);
        if (due.Count == 0) return;

        foreach (var a in due) a.Status = AuctionStatus.Active;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear(); // một bid/instance khác đã cập nhật trước -> lần quét sau xử lý
            return;
        }

        foreach (var a in due)
        {
            logger.LogInformation("Auction {AuctionId} activated", a.Id);
            await hub.Clients.All.SendAsync("AuctionStatusChanged", new { id = a.Id, status = a.Status.ToString() }, ct);
        }
    }

    public Task<List<Guid>> GetExpiredAuctionIdsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.Auctions
            .Where(a => a.Status != AuctionStatus.Ended && a.EndTime <= now)
            .Select(a => a.Id)
            .ToListAsync(ct);
    }

    private async Task PublishSafeAsync<T>(T @event, string routingKey, CancellationToken ct) where T : class
    {
        try
        {
            await publisher.PublishAsync(@event, routingKey, ct);
        }
        catch (Exception ex)
        {
            // Dữ liệu đã commit; trong production nên dùng Transactional Outbox để đảm bảo at-least-once.
            logger.LogError(ex, "Failed to publish {EventType}", typeof(T).Name);
        }
    }
}
