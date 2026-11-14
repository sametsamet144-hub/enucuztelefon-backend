using System.Text.RegularExpressions;
using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Application.Services;

public static class OfferTrust
{
    /// <summary>Doğrulanmamış teklif yayınlanmaz. Geçiş döneminde false yapılabilir, kalıcı olarak true olmalı.</summary>
    public static bool RequireVerified = false;
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(48);

    private static readonly HashSet<string> DisallowedStoreSlugs = new(StringComparer.OrdinalIgnoreCase)
    { "bim", "a101", "sok", "evkur", "yonavm", "mutluevim", "arcelik", "beko", "vestel" };

    // Seed/üretici tarafından uydurulmuş linklerin ayırt edici izleri:
    private static readonly Regex AmazonHexAsin   = new(@"/dp/B0[0-9A-F]{8}(?=$|[/?])", RegexOptions.Compiled);
    private static readonly Regex PttNineDigit    = new(@"-p-\d{9}(?=$|[/?])", RegexOptions.Compiled);
    private static readonly Regex CiceksepetiFake = new(@"-kc\d{8,10}(?=$|[/?])", RegexOptions.Compiled);
    private static readonly Regex TrendyolFake    = new(@"[?&]merchantId=9\d{2}(?=$|&)", RegexOptions.Compiled);
    private static readonly Regex TeknosaFake     = new(@"-p-(?!1250)\d{9}(?=$|[/?])", RegexOptions.Compiled);

    public static bool LooksGenerated(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        return AmazonHexAsin.IsMatch(url)
            || (url.Contains("pttavm.com") && PttNineDigit.IsMatch(url))
            || (url.Contains("ciceksepeti.com") && CiceksepetiFake.IsMatch(url))
            || (url.Contains("trendyol.com") && TrendyolFake.IsMatch(url))
            || (url.Contains("teknosa.com") && TeknosaFake.IsMatch(url))
            || (url.Contains("n11.com") && url.Contains("magaza=n11") && !url.Contains("renk="))
            || url.Contains("vodafone.com.tr")
            || url.Contains("turktelekom.com.tr");
    }

    public static bool IsDirectProductUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var u = url.ToLowerInvariant();
        if (u.Contains("/sr?q=") || u.Contains("/ara?q=") || u.Contains("/arama?") ||
            u.Contains("/s?k=") || u.Contains("?text=") || u.Contains("/search?") ||
            u.Contains("/list/?search_text=") || u.Contains("/kategori/") ||
            u.Contains("aktuel-urunler") || u.Contains("/faturaya-ek/telefonlar") ||
            u.Contains("/bireysel.turktelekom.com.tr/cihazlar") ||
            u.EndsWith("mi.com/tr/phone") || u.EndsWith("mi.com/tr/phone/") ||
            u.EndsWith("apple.com/tr/iphone") || u.EndsWith("apple.com/tr/iphone/") ||
            u.EndsWith("samsung.com/tr") || u.EndsWith("samsung.com/tr/") ||
            u.Contains("/mobil/cep-telefonu") || u.EndsWith("/phones") || u.EndsWith("/phones/"))
        {
            return false;
        }
        return true;
    }

    public static bool IsTrusted(Offer? o, DateTime nowUtc)
    {
        if (o == null || !o.IsAvailable || o.Price <= 0 || o.Store == null) return false;
        if (DisallowedStoreSlugs.Contains(o.Store.Slug)) return false;
        if (!IsDirectProductUrl(o.Url)) return false;
        if (LooksGenerated(o.Url)) return false;
        if (RequireVerified && (!o.IsVerified || nowUtc - o.LastCheckedAt > MaxAge)) return false;
        return true;
    }

    /// <summary>Medyandan çok sapan uç fiyatları (239.999 TL Xiaomi 15, 34.999 TL iPhone 15 Pro...) eler.</summary>
    public static List<Offer> RemoveOutliers(List<Offer> offers, decimal low = 0.60m, decimal high = 1.60m)
    {
        if (offers.Count < 3) return offers;
        var prices = offers.Select(o => o.Price).OrderBy(x => x).ToList();
        var median = prices[prices.Count / 2];
        return offers.Where(o => o.Price >= median * low && o.Price <= median * high).ToList();
    }
}

public interface IProductService
{
    Task<PagedResult<PhoneDto>> GetPhonesAsync(ProductFilterParams filter, CancellationToken cancellationToken = default);
    Task<Result<PhoneDto>> GetPhoneBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<StorePriceDto>>> GetOffersByPhoneSlugAsync(string slug, int? storage = null, CancellationToken cancellationToken = default);
    Task<Result<ProductPricesResponseDto>> GetPricesByPhoneIdAsync(string idOrSlug, int? storage = null, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PriceHistoryDto>>> GetPriceHistoryByPhoneSlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Result<PhonePricesResponseDto>> GetPricesByPhoneIdAsync(Guid phoneId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhoneDto>> GetDealsAsync(int limit = 6, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhoneDto>> GetCheapestAsync(int limit = 6, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhoneDto>> GetNewReleasesAsync(int limit = 6, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhoneDto>> GetPopularAsync(int limit = 6, CancellationToken cancellationToken = default);
}

public interface IBrandService
{
    Task<IReadOnlyList<BrandDto>> GetBrandsAsync(CancellationToken cancellationToken = default);
    Task<Result<BrandDto>> GetBrandBySlugAsync(string slug, CancellationToken cancellationToken = default);
}

public interface IStoreService
{
    Task<IReadOnlyList<StoreDto>> GetStoresAsync(CancellationToken cancellationToken = default);
    Task<Result<StoreDto>> GetStoreBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Result<StoreIntegrationStatus>> GetStoreStatusAsync(string slug, CancellationToken cancellationToken = default);
}

public interface ISearchService
{
    Task<IReadOnlyList<PhoneDto>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default);
}

public class ProductService : IProductService
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly ILogger<ProductService> _logger;
    private readonly IMarketOfferAggregator? _marketAggregator;

    public ProductService(
        IApplicationDbContext context,
        ICacheService cache,
        ILogger<ProductService> logger,
        IMarketOfferAggregator? marketAggregator = null)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _marketAggregator = marketAggregator;
    }

    public async Task<PagedResult<PhoneDto>> GetPhonesAsync(ProductFilterParams filter, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"products:list:{filter.Brand}:{filter.Search}:{filter.MinPrice}:{filter.MaxPrice}:{filter.Ram}:{filter.Storage}:{filter.SortBy}:{filter.Page}:{filter.PageSize}";
        var cached = await _cache.GetAsync<PagedResult<PhoneDto>>(cacheKey, cancellationToken);
        if (cached != null) return cached;

        var query = _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Specifications)
            .AsNoTracking()
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(filter.Brand))
        {
            query = query.Where(p => p.Brand.Slug == filter.Brand.ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(search) || p.Brand.Name.ToLower().Contains(search));
        }

        if (filter.MinPrice.HasValue)
        {
            query = query.Where(p => p.Variants.Any(v => v.LowestPrice >= filter.MinPrice.Value));
        }

        if (filter.MaxPrice.HasValue)
        {
            query = query.Where(p => p.Variants.Any(v => v.LowestPrice <= filter.MaxPrice.Value));
        }

        if (filter.Ram.HasValue)
        {
            query = query.Where(p => p.Variants.Any(v => v.RamGb == filter.Ram.Value));
        }

        if (filter.Storage.HasValue)
        {
            query = query.Where(p => p.Variants.Any(v => v.StorageGb == filter.Storage.Value));
        }

        if (filter.IsNew.HasValue && filter.IsNew.Value)
        {
            query = query.Where(p => p.IsNew);
        }

        if (filter.IsPriceDropped.HasValue && filter.IsPriceDropped.Value)
        {
            query = query.Where(p => p.Variants.Any(v => v.IsPriceDropped && v.PriceDropPercent >= 2));
        }

        // Sorting
        query = filter.SortBy switch
        {
            "price-asc" => query.OrderBy(p => p.Variants.Min(v => v.LowestPrice)),
            "price-desc" => query.OrderByDescending(p => p.Variants.Max(v => v.LowestPrice)),
            "newest" => query.OrderByDescending(p => p.ReleaseDate),
            "rating" => query.OrderByDescending(p => p.Rating),
            "price-drop" => query.OrderByDescending(p => p.Variants.Max(v => v.PriceDropPercent)),
            _ => query
                .OrderByDescending(p => p.Variants.Any(v => v.Offers.Any(o => o.Price > 0) || v.LowestPrice > 0))
                .ThenByDescending(p => p.Rating)
                .ThenByDescending(p => p.ReviewCount)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var page = filter.Page > 0 ? filter.Page : 1;
        var pageSize = filter.PageSize > 0 ? filter.PageSize : 20;

        var products = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = products.Select(p => MapToPhoneDto(p)).ToList();
        var result = new PagedResult<PhoneDto>(dtos, totalCount, page, pageSize);

        await _cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(10), cancellationToken);
        return result;
    }

    private static readonly Dictionary<string, string> SlugAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple-iphone-16-pro-max"] = "iphone-16-pro-max",
        ["apple-iphone-16-pro"] = "iphone-16-pro",
        ["apple-iphone-16-plus"] = "iphone-16-plus",
        ["apple-iphone-16"] = "iphone-16",
        ["apple-iphone-15-pro-max"] = "iphone-15-pro-max",
        ["apple-iphone-15-pro"] = "iphone-15-pro",
        ["apple-iphone-15-plus"] = "iphone-15-plus",
        ["apple-iphone-15"] = "iphone-15",
        ["apple-iphone-14"] = "iphone-14",
        ["apple-iphone-13"] = "iphone-13",
        ["galaxy-s24-ultra"] = "samsung-galaxy-s24-ultra",
        ["galaxy-s24-plus"] = "samsung-galaxy-s24-plus",
        ["galaxy-s24"] = "samsung-galaxy-s24",
        ["galaxy-s25-ultra"] = "samsung-galaxy-s25-ultra",
        ["galaxy-s25-plus"] = "samsung-galaxy-s25-plus",
        ["galaxy-s25"] = "samsung-galaxy-s25",
        ["galaxy-a55-5g"] = "samsung-galaxy-a55",
        ["galaxy-a35-5g"] = "samsung-galaxy-a35",
        ["redmi-note-13-pro-plus"] = "redmi-note-13-pro-plus-5g"
    };

    private static string ResolveCanonicalSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return string.Empty;
        var clean = slug.Trim().ToLowerInvariant();
        if (SlugAliases.TryGetValue(clean, out var mapped)) return mapped;

        var noStorage = System.Text.RegularExpressions.Regex.Replace(clean, @"-(64|128|256|512|1024)gb$|-1tb$", "");
        if (SlugAliases.TryGetValue(noStorage, out mapped)) return mapped;

        return noStorage;
    }

    public async Task<Result<PhoneDto>> GetPhoneBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var canonicalSlug = ResolveCanonicalSlug(slug);
        var isGuid = Guid.TryParse(slug, out var parsedGuid);
        var cacheKey = $"phone:{canonicalSlug}";
        var cached = await _cache.GetAsync<PhoneDto>(cacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        var product = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Seller)
            .Include(p => p.Specifications)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => (isGuid && p.Id == parsedGuid) || p.Slug == canonicalSlug || p.Slug == slug, cancellationToken);

        if (product == null)
        {
            return Result.Failure<PhoneDto>(Error.NotFound("Product.NotFound", $"Phone with slug '{slug}' was not found."));
        }

        var (_, existingOffers) = SelectVariantOffers(product, null, DateTime.UtcNow);
        if (existingOffers.Count <= 1 && _marketAggregator != null)
        {
            var enriched = await TryEnrichProductOffersAsync(product.Id, product.Name, product.Brand?.Name, product.Slug, cancellationToken);
            if (enriched)
            {
                product = await _context.Products
                    .Include(p => p.Brand)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Offers)
                            .ThenInclude(o => o.Store)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Offers)
                            .ThenInclude(o => o.Seller)
                    .Include(p => p.Specifications)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => (isGuid && p.Id == parsedGuid) || p.Slug == canonicalSlug || p.Slug == slug, cancellationToken);

                if (product == null)
                {
                    return Result.Failure<PhoneDto>(Error.NotFound("Product.NotFound", $"Phone with slug '{slug}' was not found."));
                }
            }
        }

        var dto = MapToPhoneDto(product);
        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(15), cancellationToken);
        return Result.Success(dto);
    }

    private async Task<bool> TryEnrichProductOffersAsync(Guid productId, string productName, string? brandName, string? productSlug, CancellationToken cancellationToken)
    {
        if (_marketAggregator == null) return false;

        try
        {
            var brand = brandName ?? "";
            var searchQuery = !string.IsNullOrWhiteSpace(brand) && productName.StartsWith(brand, StringComparison.OrdinalIgnoreCase)
                ? productName
                : $"{brand} {productName}".Trim();

            var liveOffers = await _marketAggregator.FetchOffersForQueryAsync(searchQuery, productSlug, cancellationToken);
            if (liveOffers == null || liveOffers.Count == 0) return false;

            var trackedProduct = await _context.Products
                .Include(p => p.Variants)
                    .ThenInclude(v => v.Offers)
                .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

            if (trackedProduct == null || !trackedProduct.Variants.Any()) return false;

            var stores = await _context.Stores.Where(s => s.IsActive).ToListAsync(cancellationToken);
            var storeBySlug = stores.ToDictionary(s => s.Slug, s => s, StringComparer.OrdinalIgnoreCase);

            var defaultVariant = trackedProduct.Variants.OrderBy(v => v.StorageGb).First();
            bool hasChanges = false;

            foreach (var item in liveOffers)
            {
                if (!item.IsDirectProductUrl || item.Price < 1000) continue;
                if (!EnUcuzTelefon.Shared.Utils.StoreUrlHelper.IsDirectProductUrl(item.Url)) continue;
                if (!storeBySlug.TryGetValue(item.StoreSlug, out var store)) continue;
                if (OfferTrust.LooksGenerated(item.Url)) continue;

                var targetVariant = item.StorageGb is > 0
                    ? trackedProduct.Variants.FirstOrDefault(v => v.StorageGb == item.StorageGb)
                    : defaultVariant;
                if (targetVariant == null) continue;

                var existing = targetVariant.Offers.FirstOrDefault(o => o.StoreId == store.Id);
                if (existing == null)
                {
                    var newOffer = new Offer
                    {
                        VariantId = targetVariant.Id,
                        StoreId = store.Id,
                        Price = item.Price,
                        ShippingCost = 0,
                        TotalCost = item.Price,
                        Currency = "TRY",
                        Url = item.Url,
                        IsAvailable = true,
                        IsVerified = true,
                        LastCheckedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        ShippingText = "Ücretsiz Kargo",
                        GuaranteeText = "2 Yıl Distribütör Garantili",
                        InstallmentText = "Peşin Fiyatına 3 Taksit",
                        IsOfficial = false
                    };
                    _context.Offers.Add(newOffer);
                    targetVariant.Offers.Add(newOffer);
                    hasChanges = true;
                }
                else
                {
                    // Do not re-activate an offer URL that was explicitly deactivated by the live verification engine
                    if (!existing.IsAvailable && string.Equals(existing.Url, item.Url, StringComparison.OrdinalIgnoreCase))
                        continue;

                    existing.Price = item.Price;
                    existing.TotalCost = item.Price + existing.ShippingCost;
                    existing.Url = item.Url;
                    existing.IsAvailable = true;
                    existing.IsVerified = true;
                    existing.LastCheckedAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    hasChanges = true;
                }
            }

            if (hasChanges)
            {
                foreach (var v in trackedProduct.Variants)
                {
                    var validOffers = v.Offers.Where(o => OfferTrust.IsTrusted(o, DateTime.UtcNow)).OrderBy(o => o.Price).ToList();
                    if (validOffers.Count > 0)
                    {
                        v.LowestPrice = validOffers.First().Price;
                        v.HighestPrice = validOffers.Last().Price;
                        v.LowestPriceStoreName = validOffers.First().Store?.Name ?? "";
                        v.LowestPriceUrl = validOffers.First().Url;
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);
                await _cache.RemoveAsync($"phone:{trackedProduct.Slug}", cancellationToken);
                await _cache.RemoveByPrefixAsync("phone:offers:", cancellationToken);
                await _cache.RemoveByPrefixAsync("phone:prices:", cancellationToken);
                await _cache.RemoveByPrefixAsync("products:", cancellationToken);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich offers on-demand for product ID {ProductId}", productId);
        }

        return false;
    }

    private static (ProductVariant? Variant, List<Offer> Offers) SelectVariantOffers(Product p, int? storageGb, DateTime nowUtc)
    {
        var variants = p.Variants.AsEnumerable();
        if (storageGb is > 0) variants = variants.Where(v => v.StorageGb == storageGb);

        var candidates = variants
            .Select(v => (Variant: v, Offers: BestPerStore(OfferTrust.RemoveOutliers(
                v.Offers.Where(o => OfferTrust.IsTrusted(o, nowUtc)).ToList()))))
            .Where(x => x.Offers.Count > 0)
            .ToList();

        if (candidates.Count == 0) return (null, new List<Offer>());

        // En çok mağazası olan varyant; eşitlikte en düşük depolama (giriş fiyatı)
        var best = candidates.OrderByDescending(x => x.Offers.Count).ThenBy(x => x.Variant.StorageGb).First();
        return (best.Variant, best.Offers.OrderBy(o => o.Price).ToList());
    }

    private static List<Offer> BestPerStore(List<Offer> offers) =>
        offers.GroupBy(o => o.Store!.Slug, StringComparer.OrdinalIgnoreCase)
              .Select(g => g.OrderBy(o => o.Price).First())
              .ToList();

    public async Task<Result<IReadOnlyList<StorePriceDto>>> GetOffersByPhoneSlugAsync(string slug, int? storage = null, CancellationToken cancellationToken = default)
    {
        var canonicalSlug = ResolveCanonicalSlug(slug);
        var isGuid = Guid.TryParse(slug, out var parsedGuid);
        var cacheKey = $"phone:offers:{canonicalSlug}:{storage ?? 0}";
        var cached = await _cache.GetAsync<IReadOnlyList<StorePriceDto>>(cacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        var product = await _context.Products
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Seller)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => (isGuid && p.Id == parsedGuid) || p.Slug == canonicalSlug || p.Slug == slug, cancellationToken);

        if (product == null)
        {
            return Result.Failure<IReadOnlyList<StorePriceDto>>(Error.NotFound("Product.NotFound", $"Phone with slug '{slug}' was not found."));
        }

        var (_, offers) = SelectVariantOffers(product, storage, DateTime.UtcNow);
        if (offers.Count <= 1 && _marketAggregator != null)
        {
            var enriched = await TryEnrichProductOffersAsync(product.Id, product.Name, product.Brand?.Name, product.Slug, cancellationToken);
            if (enriched)
            {
                product = await _context.Products
                    .Include(p => p.Brand)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Offers)
                            .ThenInclude(o => o.Store)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Offers)
                            .ThenInclude(o => o.Seller)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => (isGuid && p.Id == parsedGuid) || p.Slug == canonicalSlug || p.Slug == slug, cancellationToken);

                if (product != null)
                {
                    (_, offers) = SelectVariantOffers(product, storage, DateTime.UtcNow);
                }
            }
        }

        var dtos = offers.Select(o => new StorePriceDto(
            Store: o.Store!.Name,
            StoreSlug: o.Store.Slug,
            SellerName: o.Seller?.Name ?? o.Store.Name,
            SellerRating: o.Seller?.Rating ?? o.Store.ReliabilityScore,
            Price: o.Price,
            Url: o.Url,
            IsAvailable: o.IsAvailable,
            LastUpdated: o.LastCheckedAt.ToString("o"),
            ShippingText: o.ShippingText,
            GuaranteeText: o.GuaranteeText,
            InstallmentText: o.InstallmentText,
            IsOfficial: o.IsOfficial))
        .ToList();

        await _cache.SetAsync(cacheKey, dtos, TimeSpan.FromMinutes(10), cancellationToken);
        return Result.Success<IReadOnlyList<StorePriceDto>>(dtos);
    }

    public async Task<Result<ProductPricesResponseDto>> GetPricesByPhoneIdAsync(string idOrSlug, int? storage = null, CancellationToken cancellationToken = default)
    {
        var canonicalSlug = ResolveCanonicalSlug(idOrSlug);
        var isGuid = Guid.TryParse(idOrSlug, out var parsedGuid);
        var cacheKey = $"phone:prices:{canonicalSlug}:{storage ?? 0}";
        var cached = await _cache.GetAsync<ProductPricesResponseDto>(cacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        var product = await _context.Products
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Seller)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => (isGuid && p.Id == parsedGuid) || p.Slug == canonicalSlug || p.Slug == idOrSlug, cancellationToken);

        if (product == null)
        {
            return Result.Failure<ProductPricesResponseDto>(Error.NotFound("Product.NotFound", $"Phone '{idOrSlug}' was not found."));
        }

        var (_, offers) = SelectVariantOffers(product, storage, DateTime.UtcNow);
        if (offers.Count <= 1 && _marketAggregator != null)
        {
            var enriched = await TryEnrichProductOffersAsync(product.Id, product.Name, product.Brand?.Name, product.Slug, cancellationToken);
            if (enriched)
            {
                product = await _context.Products
                    .Include(p => p.Brand)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Offers)
                            .ThenInclude(o => o.Store)
                    .Include(p => p.Variants)
                        .ThenInclude(v => v.Offers)
                            .ThenInclude(o => o.Seller)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => (isGuid && p.Id == parsedGuid) || p.Slug == canonicalSlug || p.Slug == idOrSlug, cancellationToken);

                if (product != null)
                {
                    (_, offers) = SelectVariantOffers(product, storage, DateTime.UtcNow);
                }
            }
        }

        var storePrices = offers.Select(o => new StorePriceDto(
            Store: o.Store!.Name,
            StoreSlug: o.Store.Slug,
            SellerName: o.Seller?.Name ?? o.Store.Name,
            SellerRating: o.Seller?.Rating ?? o.Store.ReliabilityScore,
            Price: o.Price,
            Url: o.Url,
            IsAvailable: o.IsAvailable,
            LastUpdated: o.LastCheckedAt.ToString("o"),
            ShippingText: o.ShippingText,
            GuaranteeText: o.GuaranteeText,
            InstallmentText: o.InstallmentText,
            IsOfficial: o.IsOfficial))
        .ToList();

        decimal lowestPrice = storePrices.Count > 0 ? storePrices.Min(s => s.Price) : 0m;
        decimal highestPrice = storePrices.Count > 0 ? storePrices.Max(s => s.Price) : 0m;

        var response = new ProductPricesResponseDto(
            Id: product.Id.ToString(),
            PhoneSlug: product.Slug,
            LowestPrice: lowestPrice,
            HighestPrice: highestPrice,
            TotalStores: storePrices.Count,
            StorePrices: storePrices,
            LastUpdated: DateTime.UtcNow.ToString("o"));

        await _cache.SetAsync(cacheKey, response, TimeSpan.FromMinutes(10), cancellationToken);
        return Result.Success(response);
    }

    public async Task<Result<PhonePricesResponseDto>> GetPricesByPhoneIdAsync(Guid phoneId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"phone:prices:{phoneId}";
        var cached = await _cache.GetAsync<PhonePricesResponseDto>(cacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        var product = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Seller)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == phoneId, cancellationToken);

        if (product == null)
        {
            return Result.Failure<PhonePricesResponseDto>(Error.NotFound("Product.NotFound", $"Phone with id '{phoneId}' was not found."));
        }

        var nowUtc = DateTime.UtcNow;
        var trustedEntries = product.Variants
            .SelectMany(v => v.Offers.Select(o => new { Offer = o, Variant = v }))
            .Where(x => OfferTrust.IsTrusted(x.Offer, nowUtc))
            .GroupBy(x => x.Offer.Store!.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Offer.Price).First())
            .ToList();

        var trustedOffers = OfferTrust.RemoveOutliers(trustedEntries.Select(x => x.Offer).ToList());
        var trustedSet = new HashSet<Guid>(trustedOffers.Select(o => o.Id));

        var offerItems = trustedEntries
            .Where(x => trustedSet.Contains(x.Offer.Id))
            .OrderBy(x => x.Offer.Price)
            .Select(x => new PhoneStorePriceItemDto(
                Store: x.Offer.Store!.Name,
                StoreSlug: x.Offer.Store.Slug,
                StoreLogoUrl: x.Offer.Store.LogoUrl,
                Price: x.Offer.Price,
                OriginalPrice: x.Offer.OriginalPrice,
                Url: EnUcuzTelefon.Shared.Utils.StoreUrlHelper.ResolveSafeStoreUrl(x.Offer.Store.Slug, x.Offer.Url, product.Name),
                SellerName: x.Offer.Seller?.Name ?? x.Offer.Store.Name,
                SellerRating: x.Offer.Seller?.Rating ?? x.Offer.Store.ReliabilityScore,
                IsAvailable: x.Offer.IsAvailable,
                Variant: $"{x.Variant.StorageGb} GB" + (!string.IsNullOrWhiteSpace(x.Variant.Color) ? $" - {x.Variant.Color}" : string.Empty),
                StorageGb: x.Variant.StorageGb > 0 ? x.Variant.StorageGb : null,
                Color: string.IsNullOrWhiteSpace(x.Variant.Color) ? null : x.Variant.Color,
                ShippingText: x.Offer.ShippingText ?? x.Offer.Store.ShippingInfo,
                GuaranteeText: x.Offer.GuaranteeText ?? "2 Yıl Resmi Distribütör Garantili",
                InstallmentText: x.Offer.InstallmentText,
                IsOfficial: x.Offer.IsOfficial || x.Offer.Store.IsVerified,
                LastCheckedAt: x.Offer.LastCheckedAt
            ))
            .ToList();

        var lowestPrice = offerItems.Any()
            ? offerItems.Min(o => o.Price)
            : 0m;

        var highestPrice = offerItems.Any() 
            ? offerItems.Max(o => o.Price) 
            : (product.Variants.Any() ? product.Variants.Max(v => v.HighestPrice) : 0m);

        var response = new PhonePricesResponseDto(
            PhoneId: product.Id,
            PhoneName: product.Name,
            Slug: product.Slug,
            Brand: product.Brand?.Name ?? string.Empty,
            BrandSlug: product.Brand?.Slug ?? string.Empty,
            ImageUrl: product.ImageUrl,
            LowestPrice: lowestPrice,
            HighestPrice: highestPrice,
            TotalStores: offerItems.Select(o => o.StoreSlug).Distinct().Count(),
            TotalOffers: offerItems.Count,
            StorePrices: offerItems
        );

        await _cache.SetAsync(cacheKey, response, TimeSpan.FromMinutes(10), cancellationToken);
        return Result.Success(response);
    }

    public async Task<Result<IReadOnlyList<PriceHistoryDto>>> GetPriceHistoryByPhoneSlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var canonicalSlug = ResolveCanonicalSlug(slug);
        var isGuid = Guid.TryParse(slug, out var parsedGuid);
        var product = await _context.Products
            .Include(p => p.Variants)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => (isGuid && p.Id == parsedGuid) || p.Slug == canonicalSlug || p.Slug == slug, cancellationToken);

        if (product == null)
        {
            return Result.Failure<IReadOnlyList<PriceHistoryDto>>(Error.NotFound("Product.NotFound", $"Phone with slug '{slug}' was not found."));
        }

        var variantIds = product.Variants.Select(v => v.Id).ToList();
        var histories = await _context.PriceHistories
            .Include(h => h.Store)
            .Where(h => variantIds.Contains(h.VariantId))
            .OrderBy(h => h.Timestamp)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dtos = histories.Select(h => new PriceHistoryDto(
            h.Timestamp.ToString("o"),
            h.Price,
            h.Store.Name))
            .ToList();

        return Result.Success<IReadOnlyList<PriceHistoryDto>>(dtos);
    }

    public async Task<IReadOnlyList<PhoneDto>> GetDealsAsync(int limit = 6, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"deals:{limit}";
        var cached = await _cache.GetAsync<IReadOnlyList<PhoneDto>>(cacheKey, cancellationToken);
        if (cached != null) return cached;

        var products = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Store)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Seller)
            .Include(p => p.Specifications)
            .Where(p => p.IsActive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dtos = products.Select(p => MapToPhoneDto(p))
                           .Where(d => d.LowestPrice > 0 && d.IsPriceDropped && d.PriceDropPercent > 0)
                           .OrderByDescending(d => d.PriceDropPercent)
                           .Take(limit)
                           .ToList();

        await _cache.SetAsync(cacheKey, dtos, TimeSpan.FromMinutes(10), cancellationToken);
        return dtos;
    }

    public async Task<IReadOnlyList<PhoneDto>> GetCheapestAsync(int limit = 6, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Store)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Seller)
            .Include(p => p.Specifications)
            .Where(p => p.IsActive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var list = products.Select(p => MapToPhoneDto(p))
                           .Where(d => d.LowestPrice > 0 && d.StoresPrices.Count >= 2)
                           .OrderBy(d => d.LowestPrice)
                           .Take(limit)
                           .ToList();

        // Eğer 2+ mağazalı telefon sayısı limitin altındaysa 1+ mağazalı olanları da dahil et
        if (list.Count < limit)
        {
            var fallback = products.Select(p => MapToPhoneDto(p))
                                   .Where(d => d.LowestPrice > 0)
                                   .OrderBy(d => d.LowestPrice)
                                   .Take(limit)
                                   .ToList();
            if (fallback.Count > list.Count) list = fallback;
        }

        return list;
    }

    public async Task<IReadOnlyList<PhoneDto>> GetNewReleasesAsync(int limit = 6, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Store)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Seller)
            .Include(p => p.Specifications)
            .Where(p => p.IsActive && p.IsNew)
            .OrderByDescending(p => p.ReleaseDate)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return products.Select(p => MapToPhoneDto(p)).ToList();
    }

    public async Task<IReadOnlyList<PhoneDto>> GetPopularAsync(int limit = 6, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Store)
            .Include(p => p.Variants).ThenInclude(v => v.Offers).ThenInclude(o => o.Seller)
            .Include(p => p.Specifications)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.ReviewCount)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return products.Select(p => MapToPhoneDto(p)).ToList();
    }

    private static string? SpecStr(IReadOnlyDictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static int? SpecInt(IReadOnlyDictionary<string, string> d, string key) =>
        int.TryParse(SpecStr(d, key), out var v) ? v : null;

    private static double? SpecDbl(IReadOnlyDictionary<string, string> d, string key) =>
        double.TryParse(SpecStr(d, key), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool? SpecBool(IReadOnlyDictionary<string, string> d, string key) =>
        bool.TryParse(SpecStr(d, key), out var v) ? v : null;

    private static PhoneDto MapToPhoneDto(Product p, int? storageGb = null)
    {
        var now = DateTime.UtcNow;
        var (variant, offers) = SelectVariantOffers(p, storageGb, now);

        decimal lowestPrice = offers.Count > 0 ? offers.Min(o => o.Price) : 0m;
        // Tek teklif varsa en yüksek = en düşük. ESKİ HATA: lowest * 1.06 uydurması.
        decimal highestPrice = offers.Count > 0 ? offers.Max(o => o.Price) : 0m;
        var cheapest = offers.FirstOrDefault();

        // ESKİ HATA: lowest * 0.95 ile uydurulan 6 aylık en düşük fiyat. Worker artık PriceHistory'den hesaplıyor.
        decimal? sixMonthLowest = variant is { SixMonthLowestPrice: > 0 } && variant.SixMonthLowestPrice <= lowestPrice
            ? variant.SixMonthLowestPrice
            : null;

        var dropValid = offers.Count > 0
                        && lowestPrice > 0
                        && variant is { IsPriceDropped: true }
                        && variant.PriceDropPercent is >= 2 and <= 35;
        int? priceDropPercent = dropValid ? variant!.PriceDropPercent : null;

        var storePrices = offers.Select(o => new StorePriceDto(
            Store: o.Store!.Name,
            StoreSlug: o.Store.Slug,
            SellerName: o.Seller?.Name ?? o.Store.Name,
            SellerRating: o.Seller?.Rating ?? o.Store.ReliabilityScore,
            Price: o.Price,
            Url: o.Url,
            IsAvailable: o.IsAvailable,
            LastUpdated: o.LastCheckedAt.ToString("o"),
            ShippingText: o.ShippingText,
            GuaranteeText: o.GuaranteeText,
            InstallmentText: o.InstallmentText,
            IsOfficial: o.IsOfficial)).ToList();

        // Build Specs from Specifications collection
        var specsDict = p.Specifications
            .GroupBy(s => $"{s.Category}:{s.Key}")
            .ToDictionary(g => g.Key, g => g.First().Value);

        var display = new DisplaySpecsDto(
            Size: SpecDbl(specsDict, "display:size"),
            Resolution: SpecStr(specsDict, "display:resolution"),
            Technology: SpecStr(specsDict, "display:technology"),
            RefreshRate: SpecInt(specsDict, "display:refreshRate"),
            Brightness: SpecInt(specsDict, "display:brightness"),
            AspectRatio: SpecStr(specsDict, "display:aspectRatio"),
            Protection: SpecStr(specsDict, "display:protection"),
            Hdr: SpecStr(specsDict, "display:hdr"),
            Ppi: SpecInt(specsDict, "display:ppi"));

        var mainMp = SpecInt(specsDict, "camera:mainMp");
        var uwMp = SpecInt(specsDict, "camera:ultrawideMp");
        var teleMp = SpecInt(specsDict, "camera:telephotoMp");
        var frontMp = SpecInt(specsDict, "camera:frontMp");

        var camera = new CameraSpecsDto(
            MainCamera: mainMp.HasValue ? new MainCameraSpecsDto(
                Megapixels: mainMp.Value,
                Aperture: SpecStr(specsDict, "camera:mainAperture"),
                SensorSize: SpecStr(specsDict, "camera:sensorSize"),
                Ois: SpecBool(specsDict, "camera:ois"),
                Autofocus: SpecStr(specsDict, "camera:autofocus")) : null,
            Ultrawide: uwMp.HasValue ? new UltrawideSpecsDto(
                Megapixels: uwMp.Value,
                Aperture: SpecStr(specsDict, "camera:ultrawideAperture"),
                Fov: SpecInt(specsDict, "camera:ultrawideFov")) : null,
            Telephoto: teleMp is > 0 ? new TelephotoSpecsDto(
                Megapixels: teleMp.Value,
                Aperture: SpecStr(specsDict, "camera:telephotoAperture"),
                OpticalZoom: SpecInt(specsDict, "camera:opticalZoom"),
                MaxZoom: SpecInt(specsDict, "camera:maxZoom")) : null,
            FrontCamera: frontMp.HasValue ? new FrontCameraSpecsDto(
                Megapixels: frontMp.Value,
                Aperture: SpecStr(specsDict, "camera:frontAperture"),
                Autofocus: SpecBool(specsDict, "camera:frontAutofocus")) : null,
            VideoCapabilities: null,
            Features: null);

        var battery = new BatterySpecsDto(
            Capacity: SpecInt(specsDict, "battery:capacity"),
            WiredCharging: SpecInt(specsDict, "battery:wiredCharging"),
            WirelessCharging: SpecInt(specsDict, "battery:wirelessCharging"),
            ReverseWirelessCharging: SpecBool(specsDict, "battery:reverseWireless"),
            ChargingTime: SpecStr(specsDict, "battery:chargingTime"),
            Removable: SpecBool(specsDict, "battery:removable"));

        var performance = new PerformanceSpecsDto(
            Chipset: SpecStr(specsDict, "performance:chipset"),
            Cpu: SpecStr(specsDict, "performance:cpu"),
            Gpu: SpecStr(specsDict, "performance:gpu"),
            Ram: variant?.RamGb ?? SpecInt(specsDict, "performance:ram"),
            Storage: variant?.StorageGb ?? SpecInt(specsDict, "performance:storage"),
            StorageType: SpecStr(specsDict, "performance:storageType"),
            ExpandableStorage: SpecBool(specsDict, "performance:expandableStorage"),
            AntutuScore: SpecInt(specsDict, "performance:antutuScore"),
            GeekbenchSingle: SpecInt(specsDict, "performance:geekbenchSingle"),
            GeekbenchMulti: SpecInt(specsDict, "performance:geekbenchMulti"));

        var connectivity = new ConnectivitySpecsDto(
            Sim: SpecStr(specsDict, "connectivity:sim"),
            Nfc: SpecBool(specsDict, "connectivity:nfc"),
            Bluetooth: SpecStr(specsDict, "connectivity:bluetooth"),
            Wifi: SpecStr(specsDict, "connectivity:wifi"),
            Usb: SpecStr(specsDict, "connectivity:usb"),
            FiveG: SpecBool(specsDict, "connectivity:fiveG"),
            Infrared: SpecBool(specsDict, "connectivity:infrared"));

        var design = new DesignSpecsDto(
            Width: SpecDbl(specsDict, "design:width"),
            Height: SpecDbl(specsDict, "design:height"),
            Thickness: SpecDbl(specsDict, "design:thickness"),
            Weight: SpecDbl(specsDict, "design:weight"),
            Materials: SpecStr(specsDict, "design:materials"),
            Colors: null,
            WaterResistance: SpecStr(specsDict, "design:waterResistance"),
            Fingerprint: SpecStr(specsDict, "design:fingerprint"),
            FaceId: SpecBool(specsDict, "design:faceId"));

        var software = new SoftwareSpecsDto(
            Os: SpecStr(specsDict, "software:os"),
            UpdatePolicy: SpecStr(specsDict, "software:updatePolicy"));

        var phoneSpecs = new PhoneSpecsDto(display, camera, battery, performance, connectivity, design, software);

        return new PhoneDto(
            Id: p.Id.ToString(),
            Slug: p.Slug,
            Name: p.Name,
            Brand: p.Brand.Name,
            BrandSlug: p.Brand.Slug,
            Image: p.ImageUrl,
            Images: string.IsNullOrWhiteSpace(p.ImageUrl) ? new List<string>() : new List<string> { p.ImageUrl },
            LowestPrice: lowestPrice,
            HighestPrice: highestPrice,
            SixMonthLowestPrice: sixMonthLowest,
            PriceStore: cheapest?.Store?.Name,
            PriceUrl: cheapest?.Url,
            ReleaseDate: p.ReleaseDate?.ToString("o"),
            IsNew: p.IsNew,
            IsPriceDropped: dropValid,
            PriceDropPercent: priceDropPercent,
            Specs: phoneSpecs,
            StoresPrices: storePrices,
            Rating: p.Rating,
            ReviewCount: p.ReviewCount,
            Tags: p.Tags,
            Description: p.Description);
    }
}

public class BrandService : IBrandService
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public BrandService(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IReadOnlyList<BrandDto>> GetBrandsAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = "brands:all";
        var cached = await _cache.GetAsync<IReadOnlyList<BrandDto>>(cacheKey, cancellationToken);
        if (cached != null) return cached;

        var brands = await _context.Brands
            .Include(b => b.Products)
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var brandDtos = brands.Select(b => new BrandDto(
            b.Id.ToString(),
            b.Slug,
            b.Name,
            b.LogoUrl,
            b.Products.Count,
            b.Country,
            b.Description,
            b.Website,
            b.FoundedYear))
            .ToList();

        await _cache.SetAsync(cacheKey, brandDtos, TimeSpan.FromHours(1), cancellationToken);
        return brandDtos;
    }

    public async Task<Result<BrandDto>> GetBrandBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var brand = await _context.Brands
            .Include(b => b.Products)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Slug == slug, cancellationToken);

        if (brand == null)
        {
            return Result.Failure<BrandDto>(Error.NotFound("Brand.NotFound", $"Brand '{slug}' not found"));
        }

        return Result.Success(new BrandDto(
            brand.Id.ToString(),
            brand.Slug,
            brand.Name,
            brand.LogoUrl,
            brand.Products.Count,
            brand.Country,
            brand.Description,
            brand.Website,
            brand.FoundedYear));
    }
}

public class StoreService : IStoreService
{
    private readonly IApplicationDbContext _context;
    private readonly IStoreAdapterFactory _adapterFactory;

    public StoreService(IApplicationDbContext context, IStoreAdapterFactory adapterFactory)
    {
        _context = context;
        _adapterFactory = adapterFactory;
    }

    public async Task<IReadOnlyList<StoreDto>> GetStoresAsync(CancellationToken cancellationToken = default)
    {
        var stores = await _context.Stores
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var dtos = stores.Select(s => new StoreDto(
            s.Id.ToString(),
            s.Name,
            s.Slug,
            s.Website,
            s.Type.ToString().ToLowerInvariant(),
            s.TypeLabel,
            s.Status.ToString().ToLowerInvariant(),
            s.IntegrationMethod.ToString(),
            s.ProductCount,
            s.VerifiedPriceCount,
            s.LastSyncTime.HasValue ? s.LastSyncTime.Value.ToString("o") : DateTime.UtcNow.ToString("o"),
            s.LastErrorTime.HasValue ? s.LastErrorTime.Value.ToString("o") : null,
            s.FailedRecordCount,
            s.IsVerified,
            s.ReliabilityScore,
            s.ShippingInfo,
            s.ReturnPolicy,
            s.DistributorGuarantee))
            .ToList();

        return dtos;
    }

    public async Task<Result<StoreDto>> GetStoreBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var s = await _context.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);

        if (s == null)
        {
            return Result.Failure<StoreDto>(Error.NotFound("Store.NotFound", $"Store '{slug}' not found"));
        }

        return Result.Success(new StoreDto(
            s.Id.ToString(),
            s.Name,
            s.Slug,
            s.Website,
            s.Type.ToString().ToLowerInvariant(),
            s.TypeLabel,
            s.Status.ToString().ToLowerInvariant(),
            s.IntegrationMethod.ToString(),
            s.ProductCount,
            s.VerifiedPriceCount,
            s.LastSyncTime.HasValue ? s.LastSyncTime.Value.ToString("o") : DateTime.UtcNow.ToString("o"),
            s.LastErrorTime.HasValue ? s.LastErrorTime.Value.ToString("o") : null,
            s.FailedRecordCount,
            s.IsVerified,
            s.ReliabilityScore,
            s.ShippingInfo,
            s.ReturnPolicy,
            s.DistributorGuarantee));
    }

    public async Task<Result<StoreIntegrationStatus>> GetStoreStatusAsync(string slug, CancellationToken cancellationToken = default)
    {
        try
        {
            var adapter = _adapterFactory.GetAdapter(slug);
            return await adapter.GetIntegrationStatusAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure<StoreIntegrationStatus>(Error.Failure("Store.AdapterError", ex.Message));
        }
    }
}

public class SearchService : ISearchService
{
    private readonly IProductService _productService;

    public SearchService(IProductService productService)
    {
        _productService = productService;
    }

    public async Task<IReadOnlyList<PhoneDto>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        var result = await _productService.GetPhonesAsync(new ProductFilterParams(Search: query, PageSize: limit), cancellationToken);
        return result.Items;
    }
}
