using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Domain.Enums;
using EnUcuzTelefon.Infrastructure.Adapters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Services;

public class ScrapingBackgroundWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScrapingBackgroundWorker> _logger;

    public ScrapingBackgroundWorker(IServiceProvider serviceProvider, ILogger<ScrapingBackgroundWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 EnUcuzTelefon Scraping Background Worker started at {Time}. Immediate execution active.", DateTime.UtcNow);

        // Brief delay on initial boot to allow DB migrations and seed data initialization to complete cleanly
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var adapterFactory = scope.ServiceProvider.GetRequiredService<IStoreAdapterFactory>();
                var priceVerification = scope.ServiceProvider.GetRequiredService<IPriceVerificationService>();

                // 1. Check for pending scraping jobs
                var pendingJob = await db.ScrapingJobs
                    .Include(j => j.Store)
                    .OrderBy(j => j.StartedAt)
                    .FirstOrDefaultAsync(j => j.Status == JobStatus.Pending, stoppingToken);

                // 2. If no pending jobs, ensure active stores are synced on startup
                if (pendingJob == null)
                {
                    var hasRunning = await db.ScrapingJobs.AnyAsync(j => j.Status == JobStatus.Running, stoppingToken);
                    if (!hasRunning)
                    {
                        var storesNeedingSync = await db.Stores
                            .Where(s => s.IsActive && s.Status == StoreStatus.Active &&
                                       (s.LastSyncTime == null || s.LastSyncTime < DateTime.UtcNow.AddHours(-2)))
                            .OrderBy(s => s.LastSyncTime)
                            .Take(3)
                            .ToListAsync(stoppingToken);

                        if (storesNeedingSync.Any())
                        {
                            foreach (var store in storesNeedingSync)
                            {
                                db.ScrapingJobs.Add(new ScrapingJob
                                {
                                    StoreId = store.Id,
                                    JobType = store.LastSyncTime == null ? "InitialStartupStoreScrape" : "ScheduledTwoHourSync",
                                    Status = JobStatus.Pending,
                                    StartedAt = DateTime.UtcNow
                                });
                            }
                            await db.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("🚀 API Worker auto-dispatched scraping jobs for stores: {Stores}",
                                string.Join(", ", storesNeedingSync.Select(s => s.Slug)));
                            continue;
                        }
                    }
                }

                if (pendingJob != null)
                {
                    _logger.LogInformation("⚙️ Processing scraping job {JobId} for store '{StoreSlug}' ({StoreName})",
                        pendingJob.Id, pendingJob.Store.Slug, pendingJob.Store.Name);

                    pendingJob.Status = JobStatus.Running;
                    await db.SaveChangesAsync(stoppingToken);

                    try
                    {
                        var adapter = adapterFactory.GetAdapter(pendingJob.Store.Slug);

                        // Load all active products with brand and variants
                        var products = await db.Products
                            .Include(p => p.Brand)
                            .Include(p => p.Variants)
                                .ThenInclude(v => v.Offers)
                            .Where(p => p.IsActive)
                            .OrderByDescending(p => p.ReviewCount)
                            .ToListAsync(stoppingToken);

                        _logger.LogInformation("Found {Count} active phone models to scrape for store '{StoreSlug}'",
                            products.Count, pendingJob.Store.Slug);

                        int processed = 0;
                        int updated = 0;

                        foreach (var product in products)
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            var variants = product.Variants.ToList();
                            if (!variants.Any()) continue;

                            foreach (var variant in variants)
                            {
                                if (stoppingToken.IsCancellationRequested) break;

                                var searchQuery = BaseStoreAdapter.BuildProductSearchQuery(product.Brand?.Name, product.Name, variant.StorageGb);
                                var searchResult = await adapter.SearchProductsAsync(searchQuery, stoppingToken);

                                if (searchResult.IsSuccess && searchResult.Value.Any())
                                {
                                    var validProducts = searchResult.Value
                                        .Where(r => r.Price >= 1500 && !BaseStoreAdapter.IsAccessory(r.Title))
                                        .ToList();

                                    var raw = validProducts.FirstOrDefault() ?? searchResult.Value.First();

                                    if (raw.Price > 0)
                                    {
                                        var existingOffer = variant.Offers.FirstOrDefault(o => o.StoreId == pendingJob.StoreId);

                                        if (existingOffer == null)
                                        {
                                            var newOffer = adapter.NormalizeOffer(raw, variant.Id, pendingJob.StoreId, null);
                                            newOffer.IsAvailable = true;
                                            newOffer.IsVerified = true;
                                            newOffer.LastCheckedAt = DateTime.UtcNow;

                                            db.Offers.Add(newOffer);
                                            variant.Offers.Add(newOffer);
                                            updated++;
                                        }
                                        else
                                        {
                                            existingOffer.Price = raw.Price;
                                            existingOffer.TotalCost = raw.Price + existingOffer.ShippingCost;
                                            existingOffer.IsAvailable = true;
                                            existingOffer.Url = raw.Url;
                                            existingOffer.LastCheckedAt = DateTime.UtcNow;
                                            existingOffer.IsVerified = true;

                                            var changeResult = await priceVerification.VerifyAndRecordPriceChangeAsync(
                                                variant.Id,
                                                pendingJob.StoreId,
                                                existingOffer.SellerId,
                                                existingOffer.Id,
                                                existingOffer.Price,
                                                existingOffer.ShippingCost,
                                                existingOffer.IsAvailable,
                                                stoppingToken);

                                            if (changeResult.IsSuccess && changeResult.Value)
                                            {
                                                updated++;
                                            }
                                        }

                                        processed++;
                                    }
                                }

                                // Recalculate variant pricing based on all currently available offers
                                var activeOffers = variant.Offers
                                    .Where(o => o.IsAvailable && o.Price > 0)
                                    .ToList();

                                if (activeOffers.Any())
                                {
                                    var minOffer = activeOffers.OrderBy(o => o.Price).First();
                                    var maxOffer = activeOffers.OrderByDescending(o => o.Price).First();

                                    if (variant.LowestPrice > 0 && minOffer.Price < variant.LowestPrice)
                                    {
                                        variant.PreviousLowestPrice = variant.LowestPrice;
                                        variant.IsPriceDropped = true;
                                        variant.PriceDropPercent = (int)Math.Round((variant.LowestPrice - minOffer.Price) / variant.LowestPrice * 100);
                                    }

                                    variant.LowestPrice = minOffer.Price;
                                    variant.HighestPrice = maxOffer.Price;
                                    variant.LowestPriceStoreName = minOffer.Store?.Name ?? pendingJob.Store.Name;
                                    variant.LowestPriceUrl = minOffer.Url;
                                }

                                // Controlled rate limit between variant queries
                                await Task.Delay(100, stoppingToken);
                            }

                            // Periodically persist progress to DB every 5 products
                            if (processed > 0 && processed % 5 == 0)
                            {
                                pendingJob.ItemsProcessed = processed;
                                pendingJob.ItemsUpdated = updated;
                                await db.SaveChangesAsync(stoppingToken);
                            }
                        }

                        pendingJob.ItemsProcessed = processed;
                        pendingJob.ItemsUpdated = updated;
                        pendingJob.Status = JobStatus.Completed;
                        pendingJob.CompletedAt = DateTime.UtcNow;

                        // Update store metadata
                        pendingJob.Store.LastSyncTime = DateTime.UtcNow;
                        pendingJob.Store.ProductCount = processed;
                        pendingJob.Store.VerifiedPriceCount = updated;

                        _logger.LogInformation("✅ Completed scraping job {JobId} for store '{StoreSlug}': {Processed} items processed, {Updated} offers updated",
                            pendingJob.Id, pendingJob.Store.Slug, processed, updated);
                    }
                    catch (Exception ex)
                    {
                        pendingJob.Status = JobStatus.Failed;
                        pendingJob.ErrorMessage = ex.Message;
                        pendingJob.CompletedAt = DateTime.UtcNow;
                        _logger.LogError(ex, "Failed executing scraping job {JobId}", pendingJob.Id);
                    }

                    await db.SaveChangesAsync(stoppingToken);
                    continue; // Immediately check next job without waiting
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in worker execution cycle");
            }

            // Idle polling interval
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
