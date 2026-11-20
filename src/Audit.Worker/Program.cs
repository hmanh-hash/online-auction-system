using Audit.Worker.Data;
using Audit.Worker.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddDbContext<AuditDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("AuditDb")));
builder.Services.AddHostedService<AuditEventConsumer>();

var host = builder.Build();

// Tự động migrate DB (có retry chờ PostgreSQL)
await MigrateAsync(host.Services);

await host.RunAsync();

static async Task MigrateAsync(IServiceProvider services, int maxAttempts = 15)
{
    using var scope = services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");
    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            logger.LogInformation("Audit database migration completed.");
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Message}. Retrying in 3s...", attempt, maxAttempts, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
