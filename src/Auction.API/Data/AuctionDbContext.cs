using Auction.API.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auction.API.Data;

public class AuctionDbContext(DbContextOptions<AuctionDbContext> options) : DbContext(options)
{
    public DbSet<AuctionItem> Auctions => Set<AuctionItem>();
    public DbSet<Bid> Bids => Set<Bid>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuctionItem>(e =>
        {
            e.ToTable("Auctions");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            e.Property(x => x.StartingPrice).HasPrecision(18, 2);
            e.Property(x => x.CurrentPrice).HasPrecision(18, 2);
            e.Property(x => x.MinStepPrice).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.WinnerId).HasMaxLength(100);

            // Npgsql: uint + IsRowVersion() => cột hệ thống xmin (không tạo cột mới)
            e.Property(x => x.Version).IsRowVersion();

            e.HasIndex(x => new { x.Status, x.EndTime });

            e.HasMany(x => x.Bids)
             .WithOne(b => b.Auction)
             .HasForeignKey(b => b.AuctionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bid>(e =>
        {
            e.ToTable("Bids");
            e.HasKey(x => x.Id);
            e.Property(x => x.BidderName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.AuctionId, x.Amount });
            e.HasIndex(x => new { x.AuctionId, x.CreatedAt });
        });
    }
}

/// <summary>Dùng cho lệnh "dotnet ef migrations add" (không cần DB thật).</summary>
public class AuctionDbContextFactory : IDesignTimeDbContextFactory<AuctionDbContext>
{
    public AuctionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=auctiondb;Username=auction;Password=auction123")
            .Options;
        return new AuctionDbContext(options);
    }
}
