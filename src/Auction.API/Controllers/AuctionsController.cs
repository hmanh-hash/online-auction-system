using Auction.API.Entities;
using Auction.API.Models;
using Auction.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace Auction.API.Controllers;

[ApiController]
[Route("api/auctions")]
[Produces("application/json")]
public sealed class AuctionsController(AuctionService service) : ControllerBase
{
    /// <summary>Tạo phiên đấu giá mới.</summary>
    [HttpPost]
    [ProducesResponseType<AuctionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuctionDto>> Create(CreateAuctionRequest request, CancellationToken ct)
    {
        var dto = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>Danh sách phiên đấu giá (lọc theo status tuỳ chọn: Pending | Active | Ended).</summary>
    [HttpGet]
    public async Task<ActionResult<List<AuctionDto>>> GetAll([FromQuery] AuctionStatus? status, CancellationToken ct)
        => Ok(await service.GetAllAsync(status, ct));

    /// <summary>Chi tiết phiên đấu giá.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AuctionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var dto = await service.GetByIdAsync(id, ct);
        return dto is null
            ? NotFound(new ApiError("AUCTION_NOT_FOUND", $"Không tìm thấy phiên đấu giá {id}."))
            : Ok(dto);
    }

    /// <summary>Đặt giá mới (validate qua gRPC BidValidation.Service).</summary>
    [HttpPost("{id:guid}/bids")]
    [ProducesResponseType<PlaceBidResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiError>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PlaceBid(Guid id, PlaceBidRequest request, CancellationToken ct)
        => ToActionResult(await service.PlaceBidAsync(id, request, ct));

    /// <summary>Lịch sử trả giá của phiên (mới nhất trước).</summary>
    [HttpGet("{id:guid}/bids")]
    [ProducesResponseType<List<BidDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBids(Guid id, CancellationToken ct)
        => ToActionResult(await service.GetBidsAsync(id, ct));

    /// <summary>Kết thúc phiên đấu giá thủ công.</summary>
    [HttpPost("{id:guid}/end")]
    [ProducesResponseType<AuctionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> End(Guid id, CancellationToken ct)
        => ToActionResult(await service.EndAuctionAsync(id, "Manual", ct));

    private IActionResult ToActionResult<T>(OperationResult<T> result) =>
        result.Success
            ? StatusCode(result.StatusCode, result.Value)
            : StatusCode(result.StatusCode, result.Error);
}
