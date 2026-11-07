using System.Collections.Concurrent;
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
                options.ConnectTimeout = 2000;
                _redis = ConnectionMultiplexer.Connect(options);
                if (_redis.IsConnected)
                {
                    _db = _redis.GetDatabase();
                    _logger.LogInformation("Successfully connected to Redis distributed cache.");
                }
                else
                {
                    _logger.LogWarning("Redis server not immediately reachable. In-memory cache fallback active.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to Redis. Falling back to in-memory cache.");
            }
        }
        else
        {
            _logger.LogInformation("Redis connection string not provided. Using in-memory fallback cache.");
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
                _logger.LogWarning(ex, "Redis GET error for key {Key}, checking in-memory fallback", key);
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
                _logger.LogWarning(ex, "Redis SET error for key {Key}, using in-memory fallback", key);
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
                _logger.LogWarning(ex, "Redis DELETE error for key {Key}", key);
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
                _logger.LogWarning(ex, "Redis RemoveByPrefix error for {Prefix}", prefix);
            }
        }

        var memoryKeys = _memoryFallback.Keys.Where(k => k.StartsWith(prefix)).ToList();
        foreach (var k in memoryKeys)
        {
            _memoryFallback.TryRemove(k, out _);
        }
    }
}
