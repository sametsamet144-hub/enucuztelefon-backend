using System.Net.Sockets;
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

        if (!IsBrokerReachable(hostName, 5672))
        {
            _logger.LogInformation("RabbitMQ broker at {Host} is not active. Using in-memory message channel.", hostName);
            return;
        }

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
            _logger.LogInformation("RabbitMQ broker at {Host} unreachable ({Reason}). Using in-memory message channel.", hostName, ex.GetType().Name);
        }
    }

    private static bool IsBrokerReachable(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(350));
            if (!success) return false;
            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
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

                _logger.LogDebug("Message published to RabbitMQ queue {QueueName}", queueName);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error publishing to RabbitMQ queue {QueueName} ({Reason}). Falling back to internal queue.", queueName, ex.Message);
            }
        }

        _inMemoryQueue.Writer.TryWrite(json);
        _logger.LogDebug("Message queued to internal channel for {QueueName}", queueName);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
