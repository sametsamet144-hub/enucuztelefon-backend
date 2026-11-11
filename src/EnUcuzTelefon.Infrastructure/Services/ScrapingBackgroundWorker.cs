using System.Collections.Concurrent;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Domain.Enums;
using EnUcuzTelefon.Infrastructure.Adapters;
using EnUcuzTelefon.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnUcuzTelefon.Infrastructure.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Ayarlar (appsettings.json -> "ScrapingWorker")
// ─────────────────────────────────────────────────────────────────────────────
public sealed class ScrapingWorkerOptions
{
    // Döngü aralıkları
    public TimeSpan HealthCycleInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan JobCycleInterval { get; set; } = TimeSpan.FromSeconds(15);

    // Sağlık kontrolü
    public int StaleBatchSize { get; set; } = 20;
    public int UnavailableBatchSize { get; set; } = 5;
    public TimeSpan OfferRecheckInterval { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan UnavailableRecheckInterval { get; set; } = TimeSpan.FromHours(6);
    public int DeactivateAfterFailures { get; set; } = 3;

    // Mağaza başına istek kontrolü
    public int MaxConcurrentPerStore { get; set; } = 2;
    public TimeSpan MinDelayBetweenRequests { get; set; } = TimeSpan.FromMilliseconds(300);

    // Fiyat doğrulama
    public decimal MinLivePrice { get; set; } = 1000m;
    public decimal MinDiscoveryPrice { get; set; } = 1500m;
    public decimal MaxAutoPriceChangePercent { get; set; } = 40m; // üstü iki kez görülmeden yazılmaz

    // Fiyat düşüşü rozeti
    public int MinDropPercent { get; set; } = 2;
    public int MaxDropPercent { get; set; } = 35;
    public TimeSpan PriceDropBadgeDuration { get; set; } = TimeSpan.FromDays(7);

    // Job'lar
    public TimeSpan StoreSyncInterval { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan FailedJobCooldown { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan JobHeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public int StoresPerQueueCycle { get; set; } = 2;
    public int ProductBatchSize { get; set; } = 25;
    public int MaxConsecutiveStoreErrors { get; set; } = 25; // mağaza bizi engelliyor olabilir
}

// ─────────────────────────────────────────────────────────────────────────────
// Worker: sadece orkestrasyon. İki bağımsız döngü, biri diğerini bloke etmez.
// ─────────────────────────────────────────────────────────────────────────────
public class ScrapingBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScrapingWorkerOptions _opt;
    private readonly ILogger<ScrapingBackgroundWorker> _logger;

    public ScrapingBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ScrapingWorkerOptions> options,
        ILogger<ScrapingBackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _opt = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Migration/seed işlemlerini host başlamadan ÖNCE (Program.cs) çalıştırın;
        // sabit Task.Delay ile beklemeye gerek kalmaz.
        await Task.Yield();
        _logger.LogInformation("Scraping worker started at {Time}", DateTime.UtcNow);

        await Task.WhenAll(
            RunLoopAsync("health", _opt.HealthCycleInterval, RunHealthCycleAsync, stoppingToken),
            RunLoopAsync("jobs", _opt.JobCycleInterval, RunJobCycleAsync, stoppingToken));
    }

    private async Task RunLoopAsync(string name, TimeSpan interval, Func<CancellationToken, Task> cycle, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                try
                {
                    await cycle(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in '{Loop}' cycle. Will retry next tick.", name);
                }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // normal kapanış
        }
    }

    private async Task RunHealthCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OfferHealthSynchronizer>().RunAsync(ct);
    }

    private async Task RunJobCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StoreScrapingJobRunner>().RunAsync(ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Mağaza bazlı throttle: aynı siteye aynı anda en fazla N istek + istekler arası jitter'lı bekleme
// (Singleton olarak kaydedin.)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class StoreRequestThrottle
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ScrapingWorkerOptions _opt;

    public StoreRequestThrottle(IOptions<ScrapingWorkerOptions> options) => _opt = options.Value;

    public async Task<T> RunAsync<T>(string storeSlug, Func<Task<T>> work, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(storeSlug, _ => new SemaphoreSlim(_opt.MaxConcurrentPerStore));
        await gate.WaitAsync(ct);
        try
        {
            return await work();
        }
        finally
        {
            try
            {
                var ms = (int)_opt.MinDelayBetweenRequests.TotalMilliseconds;
                await Task.Delay(Random.Shared.Next(ms, ms * 2 + 1), ct);
            }
            catch (OperationCanceledException) { }
            gate.Release();
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Canlı sonucu offer'a uygulayan ortak mantık (health ve job aynı kodu kullanır)
// ─────────────────────────────────────────────────────────────────────────────
public static class OfferLiveUpdater
{
    /// <returns>Varyant fiyatlamasını etkileyen bir değişiklik olduysa true.</returns>
    public static bool Apply(
        Offer offer,
        LiveOfferVerificationResult r,
        string source,
        IApplicationDbContext db,
        ScrapingWorkerOptions opt,
        DateTime now,
        ILogger logger)
    {
        var affectsPricing = false;
        offer.LastCheckedAt = now;

        switch (r.Outcome)
        {
            case VerificationOutcome.Inconclusive:
                // Bot koruması / geçici hata: fiyatı, doğrulanma durumunu ve ilan durumunu DEĞİŞTİRME.
                return false;

            case VerificationOutcome.Dead:
                offer.ConsecutiveFailures++;
                if (offer.IsAvailable && offer.ConsecutiveFailures >= opt.DeactivateAfterFailures)
                {
                    logger.LogWarning("Deactivating offer {OfferId} ({Reason}): {Url}", offer.Id, r.DetectionSource, offer.Url);
                    offer.IsAvailable = false;
                    offer.IsVerified = false;
                    affectsPricing = true;
                }
                return affectsPricing;
        }

        // Confirmed
        offer.ConsecutiveFailures = 0;

        if (!string.IsNullOrWhiteSpace(r.ResolvedUrl) &&
            StoreUrlHelper.IsDirectProductUrl(r.ResolvedUrl) &&            // arama/kategori linki ASLA yazılmaz
            !string.Equals(r.ResolvedUrl, offer.Url, StringComparison.OrdinalIgnoreCase))
        {
            offer.Url = r.ResolvedUrl;
            affectsPricing = true;
        }

        if (r.InStock == false)
        {
            if (offer.IsAvailable) { offer.IsAvailable = false; affectsPricing = true; }
            return affectsPricing;
        }

        // Fiyat yalnızca ürün sayfasından geldiyse güncellenir (aggregator / korunan fiyat "doğrulama" sayılmaz).
        if (r.PriceFromProductPage && r.ExtractedPrice is { } extracted && extracted >= opt.MinLivePrice)
        {
            var live = Math.Round(extracted, 2);
            if (live != offer.Price)
            {
                var accept = !IsSuspicious(offer.Price, live, opt) || offer.PendingPrice == live;
                if (accept)
                {
                    var old = offer.Price;
                    var change = live - old;
                    db.PriceHistories.Add(new PriceHistory
                    {
                        VariantId = offer.VariantId, OfferId = offer.Id, StoreId = offer.StoreId, SellerId = offer.SellerId,
                        Price = live, PreviousPrice = old, ChangeAmount = change,
                        ChangePercent = old > 0 ? (double)(change / old * 100) : 0,
                        Timestamp = now, Source = $"{source}_{r.DetectionSource}", IsVerified = true
                    });
                    offer.Price = live;
                    offer.TotalCost = live + offer.ShippingCost;
                    offer.PendingPrice = null;
                    affectsPricing = true;
                }
                else
                {
                    offer.PendingPrice = live;
                }
            }
            else
            {
                offer.PendingPrice = null;
            }
        }

        if (!offer.IsAvailable && (r.InStock == true || (r.InStock == null && r.PriceFromProductPage)))
        {
            offer.IsAvailable = true;
            affectsPricing = true;
        }

        // "Doğrulandı" yalnızca fiyat gerçekten sayfadan okunduysa
        if (r.PriceFromProductPage) offer.IsVerified = true;

        return affectsPricing;
    }

    private static bool IsSuspicious(decimal oldPrice, decimal newPrice, ScrapingWorkerOptions opt) =>
        oldPrice > 0 && Math.Abs(newPrice - oldPrice) / oldPrice * 100 > opt.MaxAutoPriceChangePercent;
}

// ─────────────────────────────────────────────────────────────────────────────
// Varyant fiyat hesaplama: uydurma değer yok, gerçek geçmişten türetilir
// ─────────────────────────────────────────────────────────────────────────────
public static class VariantPricingCalculator
{
    public static async Task RefreshAsync(
        IApplicationDbContext db, ProductVariant variant, DateTime now, ScrapingWorkerOptions opt, CancellationToken ct)
    {
        var since = now.AddDays(-180);
        var historyLow = await db.PriceHistories
            .Where(h => h.VariantId == variant.Id && h.Timestamp >= since)
            .Select(h => (decimal?)h.Price)
            .MinAsync(ct);

        Recalculate(variant, now, historyLow, opt);
    }

    public static void Recalculate(ProductVariant variant, DateTime now, decimal? sixMonthHistoryLow, ScrapingWorkerOptions opt)
    {
        var available = variant.Offers.Where(o => o.IsAvailable && o.Price > 0).ToList();
        var direct = available
            .Where(o => MarketAggregator.IsDirectProductUrl(o.Url))
            .OrderBy(o => o.Price)
            .ToList();

        // Doğrudan ürün linki olanlar öncelikli; yoksa yalnızca stokta olan diğer teklifler.
        var pool = direct.Count > 0 ? direct : available.OrderBy(o => o.Price).ToList();
        if (pool.Count == 0) return; // stokta teklif yok: son bilinen değerler korunur (UI "stokta yok" göstermeli)

        var min = pool[0];
        var max = pool[^1];

        void ResetDrop()
        {
            variant.IsPriceDropped = false;
            variant.PriceDropPercent = 0;
            variant.PriceDropDetectedAt = null;
        }

        var oldLowest = variant.LowestPrice;
        if (oldLowest > 0 && min.Price < oldLowest)
        {
            var pct = (int)Math.Round((oldLowest - min.Price) / oldLowest * 100);
            if (pct >= opt.MinDropPercent && pct <= opt.MaxDropPercent)
            {
                variant.PreviousLowestPrice = oldLowest;
                variant.IsPriceDropped = true;
                variant.PriceDropPercent = pct;
                variant.PriceDropDetectedAt = now;
            }
            else
            {
                ResetDrop();
            }
        }
        else if (oldLowest > 0 && min.Price > oldLowest)
        {
            ResetDrop(); // fiyat tekrar yükseldi, rozet geçersiz
        }

        // Rozet süresi dolduysa kaldır
        if (variant.IsPriceDropped &&
            (variant.PriceDropDetectedAt is null || now - variant.PriceDropDetectedAt > opt.PriceDropBadgeDuration))
        {
            ResetDrop();
        }

        variant.LowestPrice = min.Price;
        variant.HighestPrice = max.Price; // tek teklif varsa en düşük = en yüksek; uydurma çarpan yok
        variant.LowestPriceStoreName = min.Store?.Name ?? "Yetkili Mağaza";
        variant.LowestPriceUrl = min.Url;
        variant.SixMonthLowestPrice = sixMonthHistoryLow.HasValue
            ? Math.Min(sixMonthHistoryLow.Value, min.Price)
            : min.Price;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Cycle 1: Teklif sağlığı + canlı fiyat senkronizasyonu (Scoped)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class OfferHealthSynchronizer
{
    private readonly IApplicationDbContext _db;
    private readonly ILiveOfferVerificationEngine _live;
    private readonly ICacheService _cache;
    private readonly StoreRequestThrottle _throttle;
    private readonly ScrapingWorkerOptions _opt;
    private readonly TimeProvider _time;
    private readonly ILogger<OfferHealthSynchronizer> _logger;

    public OfferHealthSynchronizer(
        IApplicationDbContext db,
        ILiveOfferVerificationEngine live,
        ICacheService cache,
        StoreRequestThrottle throttle,
        IOptions<ScrapingWorkerOptions> options,
        TimeProvider time,
        ILogger<OfferHealthSynchronizer> logger)
    {
        _db = db;
        _live = live;
        _cache = cache;
        _throttle = throttle;
        _opt = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var aliveDueBefore = now - _opt.OfferRecheckInterval;
        var deadDueBefore = now - _opt.UnavailableRecheckInterval;

        // Yalnızca kontrol zamanı gelmiş teklifler (aralık yoksa aynı teklifler sürekli taranır)
        var staleOffers = await OffersWithGraph()
            .Where(o => o.Price > 0 && !string.IsNullOrWhiteSpace(o.Url) && o.IsAvailable &&
                        (o.LastCheckedAt == default || o.LastCheckedAt < aliveDueBefore))
            .OrderBy(o => o.LastCheckedAt)
            .Take(_opt.StaleBatchSize)
            .ToListAsync(ct);

        // Stokta olmayanlar çok daha seyrek kontrol edilir
        var unavailableOffers = await OffersWithGraph()
            .Where(o => !string.IsNullOrWhiteSpace(o.Url) && !o.IsAvailable &&
                        (o.LastCheckedAt == default || o.LastCheckedAt < deadDueBefore))
            .OrderBy(o => o.LastCheckedAt)
            .Take(_opt.UnavailableBatchSize)
            .ToListAsync(ct);

        var offers = staleOffers.Concat(unavailableOffers).ToList();
        if (offers.Count == 0) return;

        var results = await Task.WhenAll(offers.Select(o => VerifyOneAsync(o, ct)));

        var affectedVariants = new HashSet<ProductVariant>();
        foreach (var (offer, result) in results)
        {
            now = _time.GetUtcNow().UtcDateTime;

            if (result is null)
            {
                // Geçici hata (exception): ilanı cezalandırma ama aynı döngüde tekrar vurma
                offer.LastCheckedAt = now;
                continue;
            }

            var slug = offer.Store?.Slug ?? "trendyol";
            if (OfferLiveUpdater.Apply(offer, result, "LiveEngine", _db, _opt, now, _logger) && offer.Variant != null)
                affectedVariants.Add(offer.Variant);
        }

        foreach (var variant in affectedVariants)
        {
            await VariantPricingCalculator.RefreshAsync(_db, variant, _time.GetUtcNow().UtcDateTime, _opt, ct);
        }

        await _db.SaveChangesAsync(ct);

        if (affectedVariants.Count > 0)
        {
            await _cache.RemoveByPrefixAsync("products:", ct);
            await _cache.RemoveByPrefixAsync("phone:", ct);
            await _cache.RemoveByPrefixAsync("deals:", ct);
        }
    }

    private IQueryable<Offer> OffersWithGraph() =>
        _db.Offers
            .Include(o => o.Store)
            .Include(o => o.Variant).ThenInclude(v => v.Product)
            .Include(o => o.Variant).ThenInclude(v => v.Offers).ThenInclude(x => x.Store)
            .AsSplitQuery();

    private async Task<(Offer Offer, LiveOfferVerificationResult? Result)> VerifyOneAsync(Offer offer, CancellationToken ct)
    {
        var slug = offer.Store?.Slug ?? "trendyol";
        var phoneName = offer.Variant?.Product?.Name ?? "Telefon";
        var storage = offer.Variant?.StorageGb;

        try
        {
            var res = await _throttle.RunAsync(slug,
                () => _live.VerifyAndExtractOfferAsync(offer.Url, slug, phoneName, storage, offer.Price, ct), ct);
            return (offer, res);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Tek teklifin hatası tüm batch'i düşürmez
            _logger.LogWarning(ex, "Live verification failed for offer {OfferId} ({Store})", offer.Id, slug);
            return (offer, null);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Cycle 2: Mağaza tarama job'ları (Scoped)
// ─────────────────────────────────────────────────────────────────────────────
public sealed class StoreScrapingJobRunner
{
    private readonly IApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly ScrapingWorkerOptions _opt;
    private readonly TimeProvider _time;
    private readonly ILogger<StoreScrapingJobRunner> _logger;

    public StoreScrapingJobRunner(
        IApplicationDbContext db,
        IServiceScopeFactory scopeFactory,
        ICacheService cache,
        IOptions<ScrapingWorkerOptions> options,
        TimeProvider time,
        ILogger<StoreScrapingJobRunner> logger)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _cache = cache;
        _opt = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await RecoverStuckJobsAsync(ct);
        await EnqueueScheduledJobsAsync(ct);

        var job = await TryClaimNextJobAsync(ct);
        if (job is null) return;

        await ExecuteJobAsync(job, ct);
    }

    /// <summary>Heartbeat'i kesilen (çökmüş/kapanmış) Running job'ları tekrar kuyruğa alır.</summary>
    private async Task RecoverStuckJobsAsync(CancellationToken ct)
    {
        var cutoff = _time.GetUtcNow().UtcDateTime - _opt.JobHeartbeatTimeout;
        var recovered = await _db.ScrapingJobs
            .Where(j => j.Status == JobStatus.Running && (j.HeartbeatAt == null || j.HeartbeatAt < cutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Pending), ct);

        if (recovered > 0)
            _logger.LogWarning("Recovered {Count} stuck scraping job(s) back to Pending.", recovered);
    }

    /// <summary>Mağaza bazlı kontrol: açık job'u olan veya yakın zamanda başarısız olan mağaza tekrar kuyruğa alınmaz.</summary>
    private async Task EnqueueScheduledJobsAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var syncDue = now - _opt.StoreSyncInterval;
        var failCutoff = now - _opt.FailedJobCooldown;

        var stores = await _db.Stores
            .Where(s => s.IsActive && s.Status == StoreStatus.Active &&
                        (s.LastSyncTime == null || s.LastSyncTime < syncDue) &&
                        !_db.ScrapingJobs.Any(j => j.StoreId == s.Id &&
                            (j.Status == JobStatus.Pending || j.Status == JobStatus.Running)) &&
                        !_db.ScrapingJobs.Any(j => j.StoreId == s.Id &&
                            j.Status == JobStatus.Failed && j.CompletedAt > failCutoff))
            .OrderBy(s => s.LastSyncTime)
            .Take(_opt.StoresPerQueueCycle)
            .ToListAsync(ct);

        if (stores.Count == 0) return;

        foreach (var store in stores)
        {
            _db.ScrapingJobs.Add(new ScrapingJob
            {
                StoreId = store.Id,
                JobType = store.LastSyncTime == null ? "InitialStartupStoreScrape" : "ScheduledStoreSync",
                Status = JobStatus.Pending,
                StartedAt = now
            });
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Queued sync jobs for stores: {Stores}", string.Join(", ", stores.Select(s => s.Slug)));
    }

    /// <summary>Atomik claim: Pending → Running geçişi tek SQL UPDATE ile yapılır, çoklu instance güvenlidir.</summary>
    private async Task<ScrapingJob?> TryClaimNextJobAsync(CancellationToken ct)
    {
        var candidate = await _db.ScrapingJobs
            .AsNoTracking()
            .Include(j => j.Store)
            .Where(j => j.Status == JobStatus.Pending)
            .OrderBy(j => j.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (candidate is null) return null;

        var now = _time.GetUtcNow().UtcDateTime;
        var claimed = await _db.ScrapingJobs
            .Where(j => j.Id == candidate.Id && j.Status == JobStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, JobStatus.Running)
                .SetProperty(j => j.HeartbeatAt, now), ct);

        return claimed == 1 ? candidate : null; // başka instance almış olabilir
    }

    private sealed class JobStats
    {
        public int Processed;
        public int Updated;
        public int ConsecutiveErrors;
    }

    private async Task ExecuteJobAsync(ScrapingJob job, CancellationToken ct)
    {
        var store = job.Store;
        _logger.LogInformation("Processing job {JobId} for store '{Slug}'", job.Id, store.Slug);

        var stats = new JobStats();
        try
        {
            var offset = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var count = await ProcessBatchAsync(job, offset, stats, ct);
                if (count == 0) break;
                offset += count;

                var now = _time.GetUtcNow().UtcDateTime;
                var processed = stats.Processed;
                var updated = stats.Updated;
                await _db.ScrapingJobs
                    .Where(j => j.Id == job.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.ItemsProcessed, processed)
                        .SetProperty(j => j.ItemsUpdated, updated)
                        .SetProperty(j => j.HeartbeatAt, now), ct);

                await _cache.RemoveByPrefixAsync("products:", ct);
            }

            var done = _time.GetUtcNow().UtcDateTime;
            var finalProcessed = stats.Processed;
            var finalUpdated = stats.Updated;

            await _db.ScrapingJobs
                .Where(j => j.Id == job.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Completed)
                    .SetProperty(j => j.ItemsProcessed, finalProcessed)
                    .SetProperty(j => j.ItemsUpdated, finalUpdated)
                    .SetProperty(j => j.CompletedAt, done), ct);

            await _db.Stores
                .Where(s => s.Id == job.StoreId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LastSyncTime, done)
                    .SetProperty(x => x.ProductCount, finalProcessed)
                    .SetProperty(x => x.VerifiedPriceCount, finalUpdated), ct);

            await _cache.RemoveByPrefixAsync("products:", ct);
            await _cache.RemoveByPrefixAsync("phone:", ct);
            await _cache.RemoveByPrefixAsync("deals:", ct);

            _logger.LogInformation("Completed job {JobId} for '{Slug}': {Processed} processed, {Updated} updated",
                job.Id, store.Slug, stats.Processed, stats.Updated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown: job'ı Pending'e geri al ki sonraki başlangıçta devam etsin.
            await _db.ScrapingJobs
                .Where(j => j.Id == job.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Pending), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", job.Id);
            var failedAt = _time.GetUtcNow().UtcDateTime;
            var message = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;

            await _db.ScrapingJobs
                .Where(j => j.Id == job.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, JobStatus.Failed)
                    .SetProperty(j => j.ErrorMessage, message)
                    .SetProperty(j => j.CompletedAt, failedAt), CancellationToken.None);
        }
    }

    private readonly record struct VariantResult(bool Processed, bool Updated, bool Failed, bool PricingDirty);

    /// <summary>
    /// Bir ürün sayfasını (batch) kendi scope'unda işler: change tracker şişmez, her batch ayrı kaydedilir.
    /// </summary>
    private async Task<int> ProcessBatchAsync(ScrapingJob job, int offset, JobStats stats, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IApplicationDbContext>();
        var live = sp.GetRequiredService<ILiveOfferVerificationEngine>();
        var adapterFactory = sp.GetRequiredService<IStoreAdapterFactory>();
        var throttle = sp.GetRequiredService<StoreRequestThrottle>();

        var adapter = adapterFactory.GetAdapter(job.Store.Slug);
        var storeSlug = job.Store.Slug;
        var store = await db.Stores.FirstAsync(s => s.Id == job.StoreId, ct);

        var products = await db.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Store)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.Rating)
            .ThenByDescending(p => p.ReviewCount)
            .ThenBy(p => p.Id)
            .Skip(offset)
            .Take(_opt.ProductBatchSize)
            .AsSplitQuery()
            .ToListAsync(ct);

        foreach (var product in products)
        {
            foreach (var variant in product.Variants)
            {
                ct.ThrowIfCancellationRequested();

                VariantResult result;
                try
                {
                    result = await ProcessVariantAsync(product, variant);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Variant {VariantId} of '{Product}' failed at '{Store}'", variant.Id, product.Name, storeSlug);
                    result = new VariantResult(false, false, true, false);
                }

                if (result.Processed) stats.Processed++;
                if (result.Updated) stats.Updated++;

                // Üst üste çok hata: mağaza bizi engelliyor olabilir, job'ı kontrollü şekilde bitir.
                stats.ConsecutiveErrors = result.Failed ? stats.ConsecutiveErrors + 1 : 0;
                if (stats.ConsecutiveErrors >= _opt.MaxConsecutiveStoreErrors)
                    throw new InvalidOperationException(
                        $"{stats.ConsecutiveErrors} consecutive errors at '{storeSlug}'. Store may be blocking requests.");

                if (result.PricingDirty)
                    await VariantPricingCalculator.RefreshAsync(db, variant, _time.GetUtcNow().UtcDateTime, _opt, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return products.Count;

        // ── yerel fonksiyon: adapter/live/throttle tiplerini bilmeden closure ile kullanır ──
        async Task<VariantResult> ProcessVariantAsync(Product product, ProductVariant variant)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var existing = variant.Offers.FirstOrDefault(o => o.StoreId == store.Id);

            // 1) Mevcut doğrudan teklif: canlı doğrula
            if (existing != null && MarketAggregator.IsDirectProductUrl(existing.Url))
            {
                var res = await throttle.RunAsync(storeSlug,
                    () => live.VerifyAndExtractOfferAsync(
                        existing.Url, storeSlug, product.Name, variant.StorageGb, existing.Price, ct), ct);

                var oldPrice = existing.Price;
                var dirty = OfferLiveUpdater.Apply(existing, res, "Job", db, _opt, now, _logger);
                return new VariantResult(true, existing.Price != oldPrice, !res.IsUrlAlive, dirty);
            }

            // 2) Yeni teklif keşfi
            var query = BaseStoreAdapter.BuildProductSearchQuery(product.Brand?.Name, product.Name, variant.StorageGb);
            var search = await throttle.RunAsync(storeSlug, () => adapter.SearchProductsAsync(query, ct), ct);

            if (!search.IsSuccess)
                return new VariantResult(false, false, true, false);

            // Aksesuar / çok düşük fiyat / doğrudan olmayan link ASLA fallback ile geçmez
            var raw = search.Value.FirstOrDefault(r =>
                r.Price >= _opt.MinDiscoveryPrice &&
                !BaseStoreAdapter.IsAccessory(r.Title) &&
                MarketAggregator.IsDirectProductUrl(r.Url));

            if (raw == null)
                return new VariantResult(false, false, false, false);

            if (existing == null)
            {
                var newOffer = adapter.NormalizeOffer(raw, variant.Id, store.Id, null);
                newOffer.Store = store;
                newOffer.IsAvailable = true;
                newOffer.IsVerified = true;
                newOffer.LastCheckedAt = now;
                db.Offers.Add(newOffer);
                variant.Offers.Add(newOffer);
            }
            else
            {
                existing.Price = raw.Price;
                existing.TotalCost = raw.Price + existing.ShippingCost;
                existing.Url = raw.Url;
                existing.IsAvailable = true;
                existing.IsVerified = true;
                existing.ConsecutiveFailures = 0;
                existing.LastCheckedAt = now;
            }

            return new VariantResult(true, true, false, true);
        }
    }
}
