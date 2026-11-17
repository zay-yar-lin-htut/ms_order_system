using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Messaging;

var builder = WebApplication.CreateBuilder(args);

// MySQL DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Server=localhost;Database=payment_db;User=root;Password=rootpassword;";

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Hosted Background Consumer for RabbitMQ
builder.Services.AddHostedService<OrderCreatedConsumer>();

var app = builder.Build();

// Ensure DB is created on startup & seed a default test wallet
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    try
    {
        db.Database.EnsureCreated();

        // Seed test wallet for user 'cust_001' with $100 balance
        if (!db.Wallets.Any(w => w.CustomerId == "cust_001"))
        {
            db.Wallets.Add(new Wallet { CustomerId = "cust_001", Balance = 100.00m, UpdatedAt = DateTime.UtcNow });
            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Could not auto-create database: {Message}", ex.Message);
    }
}

// 1. Health Probe
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "PaymentService" }));

// 2. Deposit / Top-up Wallet (Helper for Testing)
app.MapPost("/api/wallets/deposit", async (DepositRequest req, PaymentDbContext db) =>
{
    if (req.Amount <= 0) return Results.BadRequest(new { error = "Amount must be greater than zero." });

    var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.CustomerId == req.CustomerId);
    if (wallet == null)
    {
        wallet = new Wallet { CustomerId = req.CustomerId, Balance = req.Amount, UpdatedAt = DateTime.UtcNow };
        db.Wallets.Add(wallet);
    }
    else
    {
        wallet.Balance += req.Amount;
        wallet.UpdatedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { customerId = wallet.CustomerId, balance = wallet.Balance });
});

// 3. Get Wallet Balance
app.MapGet("/api/wallets/{customerId}", async (string customerId, PaymentDbContext db) =>
{
    var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.CustomerId == customerId);
    return wallet != null 
        ? Results.Ok(wallet) 
        : Results.NotFound(new { error = $"Wallet for {customerId} not found." });
});

app.Run();

public record DepositRequest(string CustomerId, decimal Amount);
