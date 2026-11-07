using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Domain.Enums;
using EnUcuzTelefon.Shared.Results;
using Microsoft.EntityFrameworkCore;
using MatchType = EnUcuzTelefon.Domain.Enums.MatchType;

namespace EnUcuzTelefon.Application.Services;

public interface IPriceAlertService
{
    Task<Result<PriceAlertDto>> CreateAlertAsync(CreatePriceAlertRequestDto request, string? userId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceAlertDto>> GetAlertsByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAlertAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IAdminService
{
    Task<AdminDashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default);
    Task<Result<Guid>> TriggerStoreSyncAsync(string storeSlug, CancellationToken cancellationToken = default);
    Task<Result<int>> TriggerAllStoresSyncAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceAnomaly>> GetAnomaliesAsync(CancellationToken cancellationToken = default);
    Task<Result<bool>> ResolveAnomalyAsync(Guid id, bool approve, string? adminUserId = null, CancellationToken cancellationToken = default);
}

public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request);
}

public class PriceAlertService : IPriceAlertService
{
    private readonly IApplicationDbContext _context;

    public PriceAlertService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PriceAlertDto>> CreateAlertAsync(CreatePriceAlertRequestDto request, string? userId = null, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Slug == request.PhoneSlug, cancellationToken);

        if (product == null)
        {
            return Result.Failure<PriceAlertDto>(Error.NotFound("Product.NotFound", $"Product '{request.PhoneSlug}' not found"));
        }

        var variant = product.Variants.OrderBy(v => v.LowestPrice).FirstOrDefault();
        if (variant == null)
        {
            return Result.Failure<PriceAlertDto>(Error.NotFound("Variant.NotFound", "No variant found for product"));
        }

        var alert = new PriceAlert
        {
            UserId = userId,
            ProductId = product.Id,
            VariantId = variant.Id,
            TargetPrice = request.TargetPrice,
            Email = request.Email,
            IsTriggered = false,
            IsActive = true
        };

        _context.PriceAlerts.Add(alert);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new PriceAlertDto(
            alert.Id,
            product.Slug,
            product.Name,
            alert.TargetPrice,
            variant.LowestPrice,
            alert.Email,
            alert.IsTriggered,
            alert.CreatedAt));
    }

    public async Task<IReadOnlyList<PriceAlertDto>> GetAlertsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var alerts = await _context.PriceAlerts
            .Include(a => a.Product)
            .Include(a => a.Variant)
            .Where(a => a.Email == email && a.IsActive)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        return alerts.Select(a => new PriceAlertDto(
            a.Id,
            a.Product.Slug,
            a.Product.Name,
            a.TargetPrice,
            a.Variant.LowestPrice,
            a.Email,
            a.IsTriggered,
            a.CreatedAt))
            .ToList();
    }

    public async Task<Result<bool>> DeleteAlertAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var alert = await _context.PriceAlerts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (alert == null)
        {
            return Result.Failure<bool>(Error.NotFound("Alert.NotFound", "Alert not found"));
        }

        alert.IsActive = false;
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }
}

public class AdminService : IAdminService
{
    private readonly IApplicationDbContext _context;
    private readonly IMessageBus _bus;

    public AdminService(IApplicationDbContext context, IMessageBus bus)
    {
        _context = context;
        _bus = bus;
    }

    public async Task<AdminDashboardStatsDto> GetDashboardStatsAsync(CancellationToken cancellationToken = default)
    {
        var totalPhones = await _context.Products.CountAsync(cancellationToken);
        var totalVariants = await _context.ProductVariants.CountAsync(cancellationToken);
        var totalBrands = await _context.Brands.CountAsync(cancellationToken);
        var totalStores = await _context.Stores.CountAsync(cancellationToken);
        var activeIntegrations = await _context.Stores.CountAsync(s => s.Status == StoreStatus.Active, cancellationToken);
        var totalOffers = await _context.Offers.CountAsync(cancellationToken);
        var verifiedPrices = await _context.Offers.CountAsync(o => o.IsVerified, cancellationToken);
        var priceDropCount = await _context.ProductVariants.CountAsync(v => v.IsPriceDropped, cancellationToken);
        var suspiciousPrices = await _context.PriceAnomalies.CountAsync(a => a.Status == AnomalyStatus.PendingReview, cancellationToken);
        var pendingMatches = await _context.ProductMatches.CountAsync(m => m.MatchType == MatchType.ReviewRequired, cancellationToken);

        return new AdminDashboardStatsDto(
            TotalPhones: totalPhones,
            TotalVariants: totalVariants,
            TotalBrands: totalBrands,
            TotalStores: totalStores,
            ActiveIntegrations: activeIntegrations,
            TotalOffers: totalOffers,
            VerifiedPricesCount: verifiedPrices,
            PriceDropCount: priceDropCount,
            SuspiciousPricesCount: suspiciousPrices,
            PendingMatchesCount: pendingMatches,
            LastUpdated: DateTime.UtcNow);
    }

    public async Task<Result<Guid>> TriggerStoreSyncAsync(string storeSlug, CancellationToken cancellationToken = default)
    {
        var store = await _context.Stores.FirstOrDefaultAsync(s => s.Slug == storeSlug, cancellationToken);
        if (store == null)
        {
            return Result.Failure<Guid>(Error.NotFound("Store.NotFound", $"Store '{storeSlug}' not found"));
        }

        var job = new ScrapingJob
        {
            StoreId = store.Id,
            JobType = "ManualAdminTrigger",
            Status = JobStatus.Pending,
            StartedAt = DateTime.UtcNow
        };

        _context.ScrapingJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);

        await _bus.PublishAsync("store-sync-queue", new StoreSyncMessage(job.Id, store.Slug, DateTime.UtcNow), cancellationToken);

        return Result.Success(job.Id);
    }

    public async Task<Result<int>> TriggerAllStoresSyncAsync(CancellationToken cancellationToken = default)
    {
        var activeStores = await _context.Stores
            .Where(s => s.Status == StoreStatus.Active && s.IsActive)
            .ToListAsync(cancellationToken);

        int dispatched = 0;
        foreach (var store in activeStores)
        {
            var job = new ScrapingJob
            {
                StoreId = store.Id,
                JobType = "AdminAllOrStartupSync",
                Status = JobStatus.Pending,
                StartedAt = DateTime.UtcNow
            };

            _context.ScrapingJobs.Add(job);
            await _context.SaveChangesAsync(cancellationToken);

            await _bus.PublishAsync("store-sync-queue", new StoreSyncMessage(job.Id, store.Slug, DateTime.UtcNow), cancellationToken);
            dispatched++;
        }

        return Result.Success(dispatched);
    }

    public async Task<IReadOnlyList<PriceAnomaly>> GetAnomaliesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.PriceAnomalies
            .Include(a => a.Store)
            .Include(a => a.Variant)
            .Where(a => a.Status == AnomalyStatus.PendingReview)
            .OrderByDescending(a => a.DetectedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<bool>> ResolveAnomalyAsync(Guid id, bool approve, string? adminUserId = null, CancellationToken cancellationToken = default)
    {
        var anomaly = await _context.PriceAnomalies.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (anomaly == null)
        {
            return Result.Failure<bool>(Error.NotFound("Anomaly.NotFound", "Anomaly not found"));
        }

        anomaly.Status = approve ? AnomalyStatus.Approved : AnomalyStatus.Rejected;
        anomaly.ResolvedAt = DateTime.UtcNow;
        anomaly.ResolvedByUserId = adminUserId;

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }
}
