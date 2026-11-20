using Microsoft.AspNetCore.SignalR;

namespace Auction.API.Hubs;

/// <summary>
/// SignalR Hub: client gọi JoinAuctionGroup(auctionId) để theo dõi realtime một phiên đấu giá.
/// Server push các method: BidAccepted, AuctionEnded (theo group) và AuctionCreated, AuctionUpdated,
/// AuctionStatusChanged (broadcast toàn bộ để cập nhật danh sách).
/// </summary>
public sealed class AuctionHub(ILogger<AuctionHub> logger) : Hub
{
    public static string GroupName(Guid auctionId) => $"auction-{auctionId:D}";

    public async Task JoinAuctionGroup(string auctionId)
    {
        var id = ParseId(auctionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(id));
        logger.LogInformation("Connection {ConnectionId} joined {Group}", Context.ConnectionId, GroupName(id));
    }

    public async Task LeaveAuctionGroup(string auctionId)
    {
        var id = ParseId(auctionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(id));
    }

    private static Guid ParseId(string auctionId) =>
        Guid.TryParse(auctionId, out var id) ? id : throw new HubException("auctionId không hợp lệ.");
}
