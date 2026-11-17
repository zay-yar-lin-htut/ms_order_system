using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedEvents;

namespace OrderService.Messaging;

public class PaymentResultConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentResultConsumer> _logger;

    public PaymentResultConsumer(IServiceProvider serviceProvider, IConfiguration config, ILogger<PaymentResultConsumer> logger)
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
                var paymentEvt = JsonSerializer.Deserialize<PaymentCompletedEvent>(json);

                if (paymentEvt != null)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

                    var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == paymentEvt.OrderId);
                    if (order != null)
                    {
                        order.Status = paymentEvt.IsSuccess ? "COMPLETED" : "FAILED";
                        order.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();

                        _logger.LogInformation("Order {OrderId} status updated to {Status}", order.Id, order.Status);
                    }
                }

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment result message");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        await channel.BasicConsumeAsync(
            queue: "payment.completed.queue",
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("PaymentResultConsumer started listening on payment.completed.queue");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        await channel.CloseAsync();
        await connection.CloseAsync();
    }
}
