using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedEvents;

namespace PaymentService.Messaging;

public class OrderCreatedConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<OrderCreatedConsumer> _logger;

    public OrderCreatedConsumer(IServiceProvider serviceProvider, IConfiguration config, ILogger<OrderCreatedConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMQ:Host"] ?? "localhost",
            UserName = _config["RabbitMQ:Username"] ?? "guest",
            Password = _config["RabbitMQ:Password"] ?? "guest"
        };

        IConnection? connection = null;
        IChannel? channel = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                connection = await factory.CreateConnectionAsync(stoppingToken);
                channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RabbitMQ connection failed, retrying in 5s... Error: {Message}", ex.Message);
                await Task.Delay(5000, stoppingToken);
            }
        }

        if (channel == null || connection == null) return;

        // Ensure consumer queue exists
        await channel.QueueDeclareAsync(
            queue: "order.created.queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        // Ensure result queue exists
        await channel.QueueDeclareAsync(
            queue: "payment.completed.queue",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                var orderEvt = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

                if (orderEvt != null)
                {
                    _logger.LogInformation("Processing payment for Order {OrderId}, Customer {CustomerId}, Amount {Amount}",
                        orderEvt.OrderId, orderEvt.CustomerId, orderEvt.TotalAmount);

                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

                    var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.CustomerId == orderEvt.CustomerId);

                    bool isSuccess = false;
                    string failureReason = string.Empty;

                    if (wallet == null)
                    {
                        isSuccess = false;
                        failureReason = "Customer wallet not found.";
                    }
                    else if (wallet.Balance < orderEvt.TotalAmount)
                    {
                        isSuccess = false;
                        failureReason = $"Insufficient funds. Current balance: {wallet.Balance}";
                    }
                    else
                    {
                        wallet.Balance -= orderEvt.TotalAmount;
                        wallet.UpdatedAt = DateTime.UtcNow;
                        isSuccess = true;
                    }

                    // Record transaction log
                    var txn = new PaymentTransaction
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderEvt.OrderId,
                        CustomerId = orderEvt.CustomerId,
                        Amount = orderEvt.TotalAmount,
                        PaymentStatus = isSuccess ? "SUCCESS" : "FAILED",
                        FailureReason = failureReason,
                        ProcessedAt = DateTime.UtcNow
                    };

                    db.Transactions.Add(txn);
                    await db.SaveChangesAsync();

                    // Publish completion event back to OrderService
                    var completionEvt = new PaymentCompletedEvent(
                        orderEvt.OrderId,
                        isSuccess,
                        isSuccess ? "Payment deducted successfully." : failureReason
                    );

                    var resultBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(completionEvt));
                    var properties = new BasicProperties { Persistent = true };

                    await channel.BasicPublishAsync(
                        exchange: string.Empty,
                        routingKey: "payment.completed.queue",
                        mandatory: true,
                        basicProperties: properties,
                        body: resultBytes);

                    _logger.LogInformation("Payment for Order {OrderId} finished with {Status}", orderEvt.OrderId, txn.PaymentStatus);
                }

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing order created message in PaymentService");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(
            queue: "order.created.queue",
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("OrderCreatedConsumer started listening on order.created.queue");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        await channel.CloseAsync();
        await connection.CloseAsync();
    }
}
