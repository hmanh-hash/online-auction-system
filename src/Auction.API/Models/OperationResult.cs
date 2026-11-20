namespace Auction.API.Models;

/// <summary>Kết quả nghiệp vụ, controller sẽ map sang HTTP status tương ứng.</summary>
public sealed record OperationResult<T>(bool Success, T? Value, int StatusCode, ApiError? Error)
{
    public static OperationResult<T> Ok(T value, int statusCode = StatusCodes.Status200OK) => new(true, value, statusCode, null);

    public static OperationResult<T> Fail(int statusCode, string code, string message, decimal? minAcceptable = null)
        => new(false, default, statusCode, new ApiError(code, message, minAcceptable));

    public static OperationResult<T> NotFound(Guid id)
        => Fail(StatusCodes.Status404NotFound, "AUCTION_NOT_FOUND", $"Không tìm thấy phiên đấu giá {id}.");
}
