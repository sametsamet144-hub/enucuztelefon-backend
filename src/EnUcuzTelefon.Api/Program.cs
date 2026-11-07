using System.Text;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Application.Services;
using EnUcuzTelefon.Api.Services;
using EnUcuzTelefon.Infrastructure;
using EnUcuzTelefon.Infrastructure.Identity;
using EnUcuzTelefon.Infrastructure.Persistence;
using EnUcuzTelefon.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

// 0. Load .env file if present
DotEnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

// Dynamic port binding for Cloud/PaaS platforms (Railway, Render, Fly.io, etc.)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://+:{port}");

// 1. Serilog Setup
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Extensions.Http", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// DataProtection local directory
var keysFolder = Path.Combine(AppContext.BaseDirectory, "dataprotection-keys");
Directory.CreateDirectory(keysFolder);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder));

// 2. Add Infrastructure Services (EF Core, Redis, RabbitMQ, Store Adapters, Quartz)
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IAuthService, AuthService>();

// 2.1 Register Scraping Background Worker directly inside the API
builder.Services.AddHostedService<ScrapingBackgroundWorker>();

// 3. Controllers
builder.Services.AddControllers();

// 4. JWT Authentication
var jwtKey = builder.Configuration["Jwt:SecretKey"] ?? "EnUcuzTelefonSuperSecretKey2026!MustBe32CharsLongAtLeast";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
});

// 5. Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "EnUcuzTelefon.com Enterprise API",
        Version = "v1",
        Description = "Merkezi Telefon Fiyat Karşılaştırma, Gerçek Veri Toplama ve Operasyon API'si"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 6. CORS for Frontend (Local and Production Cloud Domains)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;

            if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                var host = uri.Host;
                return host == "localhost" ||
                       host.EndsWith(".railway.app", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith(".render.com", StringComparison.OrdinalIgnoreCase) ||
                       host.EndsWith("enucuztelefon.com", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

// 7. Health Checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>("database", HealthStatus.Degraded);

var app = builder.Build();

// 8. Auto-migrate, seed database & trigger store sync on startup
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        // Apply migrations if using relational database, or ensure schema is created
        if (db.Database.IsRelational())
        {
            var hasMigrations = db.Database.GetMigrations().Any();
            if (hasMigrations)
            {
                await db.Database.MigrateAsync();
            }
            else
            {
                await db.Database.EnsureCreatedAsync();
            }
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        await DatabaseSeeder.SeedAsync(db, userManager, roleManager, logger);

        var cacheService = services.GetService<ICacheService>();
        if (cacheService != null)
        {
            await cacheService.RemoveByPrefixAsync("products:");
            await cacheService.RemoveByPrefixAsync("phone:");
            await cacheService.RemoveByPrefixAsync("deals:");
        }

        logger.LogInformation("Database initialized and verified successfully.");

        // Automatically trigger immediate store sync across all active stores on startup
        try
        {
            var adminService = services.GetRequiredService<IAdminService>();
            var syncResult = await adminService.TriggerAllStoresSyncAsync();
            if (syncResult.IsSuccess)
            {
                logger.LogInformation("🚀 API Startup Store Sync triggered successfully for {Count} active stores.", syncResult.Value);
            }
            else
            {
                logger.LogWarning("API Startup Store Sync completed with warning: {Error}", syncResult.Error.Message);
            }
        }
        catch (Exception syncEx)
        {
            logger.LogWarning(syncEx, "Startup store sync dispatch could not complete, worker will pick up next scheduled cycle.");
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Database connection not yet ready. Local operations will proceed with configured fallback.");
    }
}

// 9. Middleware Pipeline
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "EnUcuzTelefon API v1"));

app.UseSerilogRequestLogging();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

app.Run();

// For Integration Test WebApplicationFactory reference
public partial class Program { }
