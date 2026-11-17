using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using SharedEvents;

namespace OrderService.Messaging;

public class RabbitMqPublisher
{
    private readonly IConfiguration _config;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(IConfiguration config, ILogger<RabbitMqPublisher> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task PublishOrderCreatedAsync(OrderCreatedEvent evt)
    {
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMQ:Host"] ?? "localhost",
            UserName = _config["RabbitMQ:Username"] ?? "guest",
            Password = _config["RabbitMQ:Password"] ?? "guest"
        };

        try
        {
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeclareAsync(
                queue: "order.created.queue",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            var json = JsonSerializer.Serialize(evt);
            var body = Encoding.UTF8.GetBytes(json);

            var properties = new BasicProperties
            {
                Persistent = true
            };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: "order.created.queue",
                mandatory: true,
                basicProperties: properties,
                body: body);

            _logger.LogInformation("Published OrderCreatedEvent for Order {OrderId}", evt.OrderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish OrderCreatedEvent for Order {OrderId}", evt.OrderId);
            throw;
        }
    }
}
