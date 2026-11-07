using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Domain.Enums;
using EnUcuzTelefon.Infrastructure.Adapters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Worker;

public class Worker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Worker> _logger;

    public Worker(IServiceProvider serviceProvider, ILogger<Worker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EnUcuzTelefon Background Worker started at {Time}", DateTime.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var adapterFactory = scope.ServiceProvider.GetRequiredService<IStoreAdapterFactory>();
                var priceVerification = scope.ServiceProvider.GetRequiredService<IPriceVerificationService>();

                // Check for pending scraping jobs
                var pendingJob = await db.ScrapingJobs
                    .Include(j => j.Store)
                    .OrderBy(j => j.StartedAt)
                    .FirstOrDefaultAsync(j => j.Status == JobStatus.Pending, stoppingToken);

                // If no pending jobs, check if primary stores (Trendyol, Hepsiburada, n11) need initial sync
                if (pendingJob == null)
                {
                    var hasRunning = await db.ScrapingJobs.AnyAsync(j => j.Status == JobStatus.Running, stoppingToken);
                    if (!hasRunning)
                    {
                        var prioritySlugs = new[] { "trendyol", "hepsiburada", "n11" };
                        var storesNeedingSync = await db.Stores
                            .Where(s => prioritySlugs.Contains(s.Slug) && s.LastSyncTime == null)
                            .ToListAsync(stoppingToken);

                        if (storesNeedingSync.Any())
                        {
                            foreach (var store in storesNeedingSync)
                            {
                                db.ScrapingJobs.Add(new ScrapingJob
                                {
                                    StoreId = store.Id,
                                    JobType = "InitialStoreScrape",
                                    Status = JobStatus.Pending,
                                    StartedAt = DateTime.UtcNow
                                });
                            }
                            await db.SaveChangesAsync(stoppingToken);
                            _logger.LogInformation("Auto-dispatched initial scraping jobs for: {Stores}",
                                string.Join(", ", storesNeedingSync.Select(s => s.Slug)));
                            continue;
                        }
                    }
                }

                if (pendingJob != null)
                {
                    _logger.LogInformation("Processing scraping job {JobId} for store '{StoreSlug}' ({StoreName})",
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
                            if (!variants.Any())
                            {
                                // If no variants exist, continue
                                continue;
                            }

                            foreach (var variant in variants)
                            {
                                if (stoppingToken.IsCancellationRequested) break;

                                var searchQuery = $"{product.Brand.Name} {product.Name} {variant.StorageGb}GB";
                                var searchResult = await adapter.SearchProductsAsync(searchQuery, stoppingToken);
                                var existingOffer = await db.Offers
                                    .FirstOrDefaultAsync(o => o.VariantId == variant.Id && o.StoreId == pendingJob.StoreId, stoppingToken);

                                if (searchResult.IsSuccess && searchResult.Value.Any())
                                {
                                    var raw = searchResult.Value.First();
                                    bool isFallback = raw.ExternalId.EndsWith(Math.Abs(raw.Title.GetHashCode()).ToString("X8"));

                                    if (!isFallback && raw.Price > 0)
                                    {
                                        if (existingOffer == null)
                                        {
                                            var newOffer = adapter.NormalizeOffer(raw, variant.Id, pendingJob.StoreId, null);
                                            db.Offers.Add(newOffer);
                                            updated++;
                                        }
                                        else
                                        {
                                            existingOffer.Price = raw.Price;
                                            existingOffer.TotalCost = raw.Price + existingOffer.ShippingCost;
                                            existingOffer.IsAvailable = raw.InStock;
                                            existingOffer.Url = raw.Url;
                                            existingOffer.LastCheckedAt = DateTime.UtcNow;

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
                                    else if (existingOffer != null && existingOffer.IsAvailable)
                                    {
                                        existingOffer.IsAvailable = false;
                                        existingOffer.LastCheckedAt = DateTime.UtcNow;
                                    }
                                }
                                else if (existingOffer != null && existingOffer.IsAvailable)
                                {
                                    existingOffer.IsAvailable = false;
                                    existingOffer.LastCheckedAt = DateTime.UtcNow;
                                }

                                // Sync variant pricing from currently available offers
                                var activeOffers = await db.Offers
                                    .Where(o => o.VariantId == variant.Id && o.IsAvailable && o.Price > 0)
                                    .Include(o => o.Store)
                                    .ToListAsync(stoppingToken);

                                if (activeOffers.Any())
                                {
                                    var minOffer = activeOffers.OrderBy(o => o.Price).First();
                                    var maxOffer = activeOffers.OrderByDescending(o => o.Price).First();
                                    variant.LowestPrice = minOffer.Price;
                                    variant.HighestPrice = maxOffer.Price;
                                    variant.LowestPriceStoreName = minOffer.Store.Name;
                                    variant.LowestPriceUrl = minOffer.Url;
                                }
                                else
                                {
                                    variant.LowestPrice = 0;
                                    variant.HighestPrice = 0;
                                    variant.LowestPriceStoreName = null;
                                    variant.LowestPriceUrl = null;
                                }

                                // Delay between variant queries to avoid overwhelming store endpoints
                                await Task.Delay(150, stoppingToken);
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

                        _logger.LogInformation("Completed scraping job {JobId} for store '{StoreSlug}': {Processed} items processed, {Updated} offers updated",
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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in worker execution cycle");
            }

            // Polling interval
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
