using System.Net.Sockets;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Application.Services;
using EnUcuzTelefon.Infrastructure.Adapters;
using EnUcuzTelefon.Infrastructure.Cache;
using EnUcuzTelefon.Infrastructure.Identity;
using EnUcuzTelefon.Infrastructure.Matching;
using EnUcuzTelefon.Infrastructure.Messaging;
using EnUcuzTelefon.Infrastructure.Persistence;
using EnUcuzTelefon.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Quartz;

namespace EnUcuzTelefon.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 0. Load .env file if present
        DotEnvLoader.Load();

        // 1. Database Context (PostgreSQL Primary with Local Fallback if Postgres is offline)
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["DATABASE_URL"]
            ?? configuration["POSTGRES_CONNECTION_STRING"]
            ?? configuration["DB_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=enucuztelefon;Username=postgres;Password=postgres";

        var forceInMemory = string.Equals(configuration["Database:UseInMemory"], "true", StringComparison.OrdinalIgnoreCase);
        var postgresReachable = !forceInMemory && IsPostgresReachable(connectionString);

        if (postgresReachable)
        {
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(connectionString, b =>
                {
                    b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    b.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                }));
        }
        else
        {
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase("EnUcuzTelefonDb"));
        }

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        // 2. Identity
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // 3. Cache & Messaging
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IMessageBus, RabbitMqMessageBus>();

        // 4. HTTP Client for Adapters
        services.AddHttpClient();

        // 5. Store Adapters (Strict 29 Whitelisted Turkish Stores)
        services.AddScoped<IStoreAdapter, HepsiburadaAdapter>();
        services.AddScoped<IStoreAdapter, TrendyolAdapter>();
        services.AddScoped<IStoreAdapter, AmazonTurkeyAdapter>();
        services.AddScoped<IStoreAdapter, N11Adapter>();
        services.AddScoped<IStoreAdapter, PttAvmAdapter>();
        services.AddScoped<IStoreAdapter, CicekSepetiAdapter>();
        services.AddScoped<IStoreAdapter, MediaMarktAdapter>();
        services.AddScoped<IStoreAdapter, TeknosaAdapter>();
        services.AddScoped<IStoreAdapter, VatanAdapter>();
        services.AddScoped<IStoreAdapter, TurkcellPasajAdapter>();
        services.AddScoped<IStoreAdapter, VodafoneAdapter>();
        services.AddScoped<IStoreAdapter, TurkTelekomAdapter>();
        services.AddScoped<IStoreAdapter, AppleStoreAdapter>();
        services.AddScoped<IStoreAdapter, SamsungStoreAdapter>();
        services.AddScoped<IStoreAdapter, XiaomiStoreAdapter>();
        services.AddScoped<IStoreAdapter, HuaweiStoreAdapter>();
        services.AddScoped<IStoreAdapter, HonorStoreAdapter>();
        services.AddScoped<IStoreAdapter, TecnoStoreAdapter>();
        services.AddScoped<IStoreAdapter, InfinixStoreAdapter>();
        services.AddScoped<IStoreAdapter, VivoStoreAdapter>();
        services.AddScoped<IStoreAdapter, EvkurAdapter>();
        services.AddScoped<IStoreAdapter, YonAvmAdapter>();
        services.AddScoped<IStoreAdapter, MutluEvimAdapter>();
        services.AddScoped<IStoreAdapter, ArcelikAdapter>();
        services.AddScoped<IStoreAdapter, BekoAdapter>();
        services.AddScoped<IStoreAdapter, VestelAdapter>();
        services.AddScoped<IStoreAdapter, A101Adapter>();
        services.AddScoped<IStoreAdapter, BimAdapter>();
        services.AddScoped<IStoreAdapter, SokAdapter>();

        services.AddScoped<IStoreAdapterFactory, StoreAdapterFactory>();

        // 6. Business Engines
        services.AddScoped<IProductMatchingEngine, ProductMatchingEngine>();
        services.AddScoped<IPriceVerificationService, PriceVerificationService>();
        services.AddScoped<IPriceAlertNotifier, PriceAlertNotifier>();

        // 7. Application Services
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IStoreService, StoreService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IPriceAlertService, PriceAlertService>();
        services.AddScoped<IAdminService, AdminService>();

        // 8. Quartz.NET Scheduling
        services.AddQuartz(q =>
        {
            q.UseSimpleTypeLoader();
            q.UseInMemoryStore();
        });

        services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }

    private static bool IsPostgresReachable(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var host = builder.Host ?? "localhost";
            var port = builder.Port > 0 ? builder.Port : 5432;

            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(400));
            if (!success) return false;
            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
