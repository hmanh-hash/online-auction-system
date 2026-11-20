using Audit.Worker.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Audit.Worker.Data;

public class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<BidHistory> BidHistories => Set<BidHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => new { x.AuctionId, x.CreatedAt });
        });

        modelBuilder.Entity<BidHistory>(e =>
        {
            e.ToTable("BidHistories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ProductName).HasMaxLength(200);
            e.Property(x => x.BidderName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.PreviousPrice).HasPrecision(18, 2);
            e.HasIndex(x => new { x.AuctionId, x.BidAt });
        });
    }
}

/// <summary>Dùng cho lệnh "dotnet ef migrations add".</summary>
public class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=auditdb;Username=auction;Password=auction123")
            .Options;
        return new AuditDbContext(options);
    }
}
