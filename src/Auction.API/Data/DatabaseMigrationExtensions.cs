using Microsoft.EntityFrameworkCore;

namespace Auction.API.Data;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Tự động chạy EF Core migration khi khởi động, có retry để chờ PostgreSQL sẵn sàng.
    /// </summary>
    public static async Task MigrateDatabaseAsync<TContext>(this IServiceProvider services, int maxAttempts = 15)
        where TContext : DbContext
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("Database migration for {Context} completed.", typeof(TContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Message}. Retrying in 3s...",
                    attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
