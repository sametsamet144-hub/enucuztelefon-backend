using System.Text.RegularExpressions;

namespace EnUcuzTelefon.Infrastructure.Persistence;

public static class DotEnvLoader
{
    private static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        var searchedDirs = new List<DirectoryInfo>();

        var dir = current;
        while (dir != null && searchedDirs.Count < 5)
        {
            searchedDirs.Add(dir);
            dir = dir.Parent;
        }

        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        if (!searchedDirs.Any(d => d.FullName.Equals(baseDir.FullName, StringComparison.OrdinalIgnoreCase)))
        {
            searchedDirs.Add(baseDir);
        }

        foreach (var d in searchedDirs)
        {
            var envFile = Path.Combine(d.FullName, ".env");
            if (File.Exists(envFile))
            {
                ApplyEnvFile(envFile);
                break;
            }
        }
    }

    public static string NormalizePostgresConnectionString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(raw);
                var userInfo = uri.UserInfo.Split(':');
                var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
                var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
                var host = uri.Host;
                var port = uri.Port > 0 ? uri.Port : 5432;
                var database = uri.AbsolutePath.TrimStart('/');

                return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;";
            }
            catch
            {
                return raw;
            }
        }
        return raw;
    }

    private static void ApplyEnvFile(string filePath)
    {
        try
        {
            foreach (var line in File.ReadAllLines(filePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                var match = Regex.Match(trimmed, @"^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$");
                if (match.Success)
                {
                    var key = match.Groups[1].Value;
                    var val = match.Groups[2].Value.Trim();

                    // Strip wrapping quotes
                    if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
                    {
                        val = val.Substring(1, val.Length - 2);
                    }

                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, val);
                    }

                    // Database URL and aliases
                    if (key.Equals("DATABASE_URL", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("POSTGRES_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("DB_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("ConnectionStrings__DefaultConnection", StringComparison.OrdinalIgnoreCase))
                    {
                        var normalized = NormalizePostgresConnectionString(val);
                        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", normalized);
                    }
                    else if (key.Equals("REDIS_URL", StringComparison.OrdinalIgnoreCase) ||
                             key.Equals("REDIS_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Redis")))
                        {
                            Environment.SetEnvironmentVariable("ConnectionStrings__Redis", val);
                        }
                    }
                    else if (key.Equals("RABBITMQ_HOST", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RabbitMQ__HostName")))
                        {
                            Environment.SetEnvironmentVariable("RabbitMQ__HostName", val);
                        }
                    }
                    else if (key.Equals("JWT_SECRET", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Jwt__SecretKey")))
                        {
                            Environment.SetEnvironmentVariable("Jwt__SecretKey", val);
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently ignore if unreadable
        }
    }
}
