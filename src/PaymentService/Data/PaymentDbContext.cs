using Microsoft.EntityFrameworkCore;

namespace PaymentService.Data;

public class Wallet
{
    public string CustomerId { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class PaymentTransaction
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentStatus { get; set; } = "SUCCESS"; // SUCCESS, FAILED
    public string FailureReason { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<PaymentTransaction> Transactions => Set<PaymentTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>(b =>
        {
            b.HasKey(w => w.CustomerId);
            b.Property(w => w.CustomerId).HasMaxLength(100);
            b.Property(w => w.Balance).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PaymentTransaction>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.CustomerId).IsRequired().HasMaxLength(100);
            b.Property(t => t.Amount).HasPrecision(18, 2);
            b.Property(t => t.PaymentStatus).IsRequired().HasMaxLength(50);
        });
    }
}
