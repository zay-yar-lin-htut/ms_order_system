using Microsoft.EntityFrameworkCore;

namespace OrderService.Data;

public class Order
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING, COMPLETED, FAILED
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.CustomerId).IsRequired().HasMaxLength(100);
            b.Property(x => x.TotalAmount).HasPrecision(18, 2);
            b.Property(x => x.Status).IsRequired().HasMaxLength(50);
        });
    }
}
