using Microsoft.Extensions.Options;

namespace Auction.API.Services;

public sealed class AuctionLifecycleOptions
{
    public const string SectionName = "AuctionLifecycle";
    public int ScanIntervalSeconds { get; set; } = 2;
}

/// <summary>
/// Background Service quét định kỳ:
///  - Pending đã tới StartTime  => Active
///  - Chưa Ended mà đã quá EndTime => kết thúc phiên, chọn người thắng, phát AuctionEnded
/// </summary>
public sealed class AuctionLifecycleWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AuctionLifecycleOptions> options,
    ILogger<AuctionLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.ScanIntervalSeconds));
        using var timer = new PeriodicTimer(interval);
        logger.LogInformation("Auction lifecycle worker started (interval {Interval}s)", interval.TotalSeconds);

        try
        {
            do
            {
                try
                {
                    await ScanAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Auction lifecycle scan failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AuctionService>();

        await service.ActivateDueAuctionsAsync(ct);

        foreach (var id in await service.GetExpiredAuctionIdsAsync(ct))
        {
            await service.EndAuctionAsync(id, "Scheduler", ct);
        }
    }
}
