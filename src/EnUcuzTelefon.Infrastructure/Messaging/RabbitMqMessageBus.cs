using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EnUcuzTelefon.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace EnUcuzTelefon.Infrastructure.Messaging;

public class RabbitMqMessageBus : IMessageBus, IDisposable
{
    private readonly IConnection? _connection;
    private readonly IModel? _channel;
    private readonly ILogger<RabbitMqMessageBus> _logger;
    private static readonly Channel<string> _inMemoryQueue = Channel.CreateUnbounded<string>();

    public RabbitMqMessageBus(IConfiguration configuration, ILogger<RabbitMqMessageBus> logger)
    {
        _logger = logger;
        var hostName = configuration["RabbitMQ:HostName"] ?? "localhost";
        var userName = configuration["RabbitMQ:UserName"] ?? "guest";
        var password = configuration["RabbitMQ:Password"] ?? "guest";

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = hostName,
                UserName = userName,
                Password = password,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(2),
                AutomaticRecoveryEnabled = true
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _logger.LogInformation("Successfully connected to RabbitMQ broker at {Host}", hostName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ broker at {Host} unreachable. Using in-memory channel.", hostName);
        }
    }

    public Task PublishAsync<T>(string queueName, T message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message);

        if (_channel != null && _channel.IsOpen)
        {
            try
            {
                _channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                var body = Encoding.UTF8.GetBytes(json);
                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;

                _channel.BasicPublish(
                    exchange: string.Empty,
                    routingKey: queueName,
                    basicProperties: properties,
                    body: body);

                _logger.LogInformation("Message published to RabbitMQ queue {QueueName}", queueName);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error publishing to RabbitMQ queue {QueueName}. Falling back to internal queue.", queueName);
            }
        }

        _inMemoryQueue.Writer.TryWrite(json);
        _logger.LogInformation("Message queued to internal channel for {QueueName}", queueName);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
