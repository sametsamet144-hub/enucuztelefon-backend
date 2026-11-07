using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using EnUcuzTelefon.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EnUcuzTelefon.Infrastructure.Cache;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly IDatabase? _db;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly ConcurrentDictionary<string, (object Value, DateTime Expiry)> _memoryFallback = new();

    public RedisCacheService(IConfiguration configuration, ILogger<RedisCacheService> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var options = ConfigurationOptions.Parse(connectionString);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = 1000;

                if (IsEndpointReachable(options))
                {
                    _redis = ConnectionMultiplexer.Connect(options);
                    if (_redis.IsConnected)
                    {
                        _db = _redis.GetDatabase();
                        _logger.LogInformation("Successfully connected to Redis distributed cache.");
                    }
                    else
                    {
                        _logger.LogInformation("Redis server not immediately reachable. In-memory cache fallback active.");
                    }
                }
                else
                {
                    _logger.LogInformation("Redis server is not active. In-memory cache fallback active.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Redis server unavailable ({Reason}). Falling back to in-memory cache.", ex.GetType().Name);
            }
        }
        else
        {
            _logger.LogInformation("Redis connection string not provided. Using in-memory fallback cache.");
        }
    }

    private static bool IsEndpointReachable(ConfigurationOptions options)
    {
        try
        {
            var ep = options.EndPoints.FirstOrDefault();
            if (ep is System.Net.DnsEndPoint dns)
            {
                using var client = new TcpClient();
                var result = client.BeginConnect(dns.Host, dns.Port == 0 ? 6379 : dns.Port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(350));
                if (!success) return false;
                client.EndConnect(result);
                return true;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_db != null && _redis?.IsConnected == true)
        {
            try
            {
                var value = await _db.StringGetAsync(key);
                if (value.HasValue)
                {
                    return JsonSerializer.Deserialize<T>(value!);
                }
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis GET error for key {Key}, checking in-memory fallback", key);
            }
        }

        if (_memoryFallback.TryGetValue(key, out var item))
        {
            if (DateTime.UtcNow <= item.Expiry)
            {
                return (T)item.Value;
            }
            _memoryFallback.TryRemove(key, out _);
        }

        return default;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var exp = expiration ?? TimeSpan.FromMinutes(30);

        if (_db != null && _redis?.IsConnected == true)
        {
            try
            {
                var json = JsonSerializer.Serialize(value);
                await _db.StringSetAsync(key, json, exp);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis SET error for key {Key}, using in-memory fallback", key);
            }
        }

        if (value is not null)
        {
            _memoryFallback[key] = (value, DateTime.UtcNow.Add(exp));
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_db != null && _redis?.IsConnected == true)
        {
            try
            {
                await _db.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis DELETE error for key {Key}", key);
            }
        }

        _memoryFallback.TryRemove(key, out _);
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (_redis != null && _redis.IsConnected)
        {
            try
            {
                var endpoints = _redis.GetEndPoints();
                foreach (var endpoint in endpoints)
                {
                    var server = _redis.GetServer(endpoint);
                    var keys = server.Keys(pattern: $"{prefix}*").ToArray();
                    if (_db != null && keys.Length > 0)
                    {
                        await _db.KeyDeleteAsync(keys);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis RemoveByPrefix error for {Prefix}", prefix);
            }
        }

        var memoryKeys = _memoryFallback.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var k in memoryKeys)
        {
            _memoryFallback.TryRemove(k, out _);
        }
    }
}
