using Microsoft.EntityFrameworkCore;

namespace Pulse.Infrastructure.Persistence;

public class PulseDbContext : DbContext
{
    public PulseDbContext(DbContextOptions<PulseDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<InventoryLedger> InventoryLedgers => Set<InventoryLedger>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Sku).IsUnique();
            b.Property(x => x.Sku).HasMaxLength(64).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Inventory>(b =>
        {
            b.HasKey(x => x.Sku);
            b.Property(x => x.Sku).HasMaxLength(64);
            b.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.OrderNo).IsUnique();
            b.HasIndex(x => x.IdempotencyKey).IsUnique();
            b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            b.Property(x => x.OrderNo).HasMaxLength(32);
            b.Property(x => x.IdempotencyKey).HasMaxLength(128);
            b.Property(x => x.TotalAmount).HasPrecision(18, 2);
            b.Property(x => x.Status).HasConversion<int>();
            b.Property(x => x.Version).IsConcurrencyToken();
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.OrderId);
            b.Property(x => x.Sku).HasMaxLength(64);
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
            b.HasOne(x => x.Order)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryLedger>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.Sku, x.CreatedAtUtc });
            b.Property(x => x.Sku).HasMaxLength(64);
            b.Property(x => x.ChangeType).HasMaxLength(32);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.EventId).IsUnique();
            b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            b.Property(x => x.EventId).HasMaxLength(64);
            b.Property(x => x.RoutingKey).HasMaxLength(128);
            b.Property(x => x.Status).HasMaxLength(32);
        });
    }
}
