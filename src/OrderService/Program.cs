using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Messaging;
using SharedEvents;

var builder = WebApplication.CreateBuilder(args);

// MySQL DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Server=localhost;Database=order_db;User=root;Password=rootpassword;";

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// RabbitMQ Services
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<PaymentResultConsumer>();

var app = builder.Build();

// Ensure DB is created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Could not auto-create database: {Message}", ex.Message);
    }
}

// 1. Health Probe
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "OrderService" }));

// 2. Create Order
app.MapPost("/api/orders", async (CreateOrderRequest request, OrderDbContext db, RabbitMqPublisher publisher) =>
{
    if (request.TotalAmount <= 0)
    {
        return Results.BadRequest(new { error = "TotalAmount must be greater than zero." });
    }

    var order = new Order
    {
        Id = Guid.NewGuid(),
        CustomerId = request.CustomerId,
        TotalAmount = request.TotalAmount,
        Status = "PENDING",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    // Publish event to RabbitMQ
    await publisher.PublishOrderCreatedAsync(new OrderCreatedEvent(order.Id, order.CustomerId, order.TotalAmount));

    return Results.Accepted($"/api/orders/{order.Id}", new
    {
        orderId = order.Id,
        status = order.Status,
        message = "Order accepted and is currently PENDING payment processing."
    });
});

// 3. Get Order by ID
app.MapGet("/api/orders/{id:guid}", async (Guid id, OrderDbContext db) =>
{
    var order = await db.Orders.FindAsync(id);
    return order != null 
        ? Results.Ok(order) 
        : Results.NotFound(new { error = $"Order {id} not found." });
});

app.Run();

public record CreateOrderRequest(string CustomerId, decimal TotalAmount);
