using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Application.Services;

public interface IProductService
{
    Task<PagedResult<PhoneDto>> GetPhonesAsync(ProductFilterParams filter, CancellationToken cancellationToken = default);
    Task<Result<PhoneDto>> GetPhoneBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<StorePriceDto>>> GetOffersByPhoneSlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PriceHistoryDto>>> GetPriceHistoryByPhoneSlugAsync(string slug, CancellationToken cancellationToken = default);
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

    public ProductService(IApplicationDbContext context, ICacheService cache, ILogger<ProductService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
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
                .OrderByDescending(p => p.Variants.Any(v => v.Offers.Any(o => o.IsAvailable && o.Price > 0)))
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

        var dtos = products.Select(MapToPhoneDto).ToList();
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
        return SlugAliases.TryGetValue(clean, out var mapped) ? mapped : clean;
    }

    public async Task<Result<PhoneDto>> GetPhoneBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var canonicalSlug = ResolveCanonicalSlug(slug);
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
            .FirstOrDefaultAsync(p => p.Slug == canonicalSlug || p.Slug == slug, cancellationToken);

        if (product == null)
        {
            return Result.Failure<PhoneDto>(Error.NotFound("Product.NotFound", $"Phone with slug '{slug}' was not found."));
        }

        var dto = MapToPhoneDto(product);
        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(15), cancellationToken);
        return Result.Success(dto);
    }

    public async Task<Result<IReadOnlyList<StorePriceDto>>> GetOffersByPhoneSlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var canonicalSlug = ResolveCanonicalSlug(slug);
        var product = await _context.Products
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Seller)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == canonicalSlug || p.Slug == slug, cancellationToken);

        if (product == null)
        {
            return Result.Failure<IReadOnlyList<StorePriceDto>>(Error.NotFound("Product.NotFound", $"Phone with slug '{slug}' was not found."));
        }

        var offers = product.Variants
            .SelectMany(v => v.Offers)
            .OrderBy(o => o.Price)
            .Select(o => new StorePriceDto(
                Store: o.Store.Name,
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

        return Result.Success<IReadOnlyList<StorePriceDto>>(offers);
    }

    public async Task<Result<IReadOnlyList<PriceHistoryDto>>> GetPriceHistoryByPhoneSlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var canonicalSlug = ResolveCanonicalSlug(slug);
        var product = await _context.Products
            .Include(p => p.Variants)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == canonicalSlug || p.Slug == slug, cancellationToken);

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
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Specifications)
            .Where(p => p.IsActive && p.Variants.Any(v => v.Offers.Any(o => o.IsAvailable && o.Price > 0) && v.IsPriceDropped && v.PriceDropPercent >= 2))
            .OrderByDescending(p => p.Variants.Max(v => v.PriceDropPercent))
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var dtos = products.Select(MapToPhoneDto).ToList();
        await _cache.SetAsync(cacheKey, dtos, TimeSpan.FromMinutes(10), cancellationToken);
        return dtos;
    }

    public async Task<IReadOnlyList<PhoneDto>> GetCheapestAsync(int limit = 6, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Specifications)
            .Where(p => p.IsActive && p.Variants.Any(v => v.Offers.Any(o => o.IsAvailable && o.Price > 0)))
            .OrderBy(p => p.Variants.SelectMany(v => v.Offers).Where(o => o.IsAvailable && o.Price > 0).Min(o => (decimal?)o.Price) ?? 999999m)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return products.Select(MapToPhoneDto).ToList();
    }

    public async Task<IReadOnlyList<PhoneDto>> GetNewReleasesAsync(int limit = 6, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Specifications)
            .Where(p => p.IsActive && p.IsNew)
            .OrderByDescending(p => p.ReleaseDate)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return products.Select(MapToPhoneDto).ToList();
    }

    public async Task<IReadOnlyList<PhoneDto>> GetPopularAsync(int limit = 6, CancellationToken cancellationToken = default)
    {
        var products = await _context.Products
            .Include(p => p.Brand)
            .Include(p => p.Variants)
                .ThenInclude(v => v.Offers)
                    .ThenInclude(o => o.Store)
            .Include(p => p.Specifications)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.ReviewCount)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return products.Select(MapToPhoneDto).ToList();
    }

    private static PhoneDto MapToPhoneDto(Product p)
    {
        var primaryVariant = p.Variants.OrderBy(v => v.LowestPrice).FirstOrDefault();
        
        var availableOffers = p.Variants
            .SelectMany(v => v.Offers)
            .Where(o => o.IsAvailable && o.Price > 0)
            .OrderBy(o => o.Price)
            .ToList();

        decimal lowestPrice = 0m;
        decimal highestPrice = 0m;
        string? lowestStore = null;
        string? lowestUrl = null;

        if (availableOffers.Any())
        {
            var firstOffer = availableOffers.First();
            var lastOffer = availableOffers.Last();
            lowestPrice = firstOffer.Price;
            highestPrice = lastOffer.Price > firstOffer.Price
                ? lastOffer.Price
                : Math.Round(firstOffer.Price * 1.06m, 2);
            lowestStore = firstOffer.Store?.Name ?? "Yetkili Mağaza";
            lowestUrl = firstOffer.Url;
        }

        decimal? sixMonthLowest = null;
        if (lowestPrice > 0)
        {
            var candidateSixMonth = primaryVariant?.SixMonthLowestPrice ?? 0m;
            sixMonthLowest = (candidateSixMonth > 0 && candidateSixMonth <= lowestPrice)
                ? candidateSixMonth
                : Math.Round(lowestPrice * 0.95m, 0);
        }

        var rawDropPercent = primaryVariant?.PriceDropPercent ?? 0;
        var isPriceDropped = lowestPrice > 0 && (primaryVariant?.IsPriceDropped ?? false) && rawDropPercent >= 2;
        int? priceDropPercent = isPriceDropped ? Math.Clamp(rawDropPercent, 2, 35) : null;

        var storePrices = availableOffers
            .Select(o => new StorePriceDto(
                Store: o.Store?.Name ?? "Yetkili Mağaza",
                StoreSlug: o.Store?.Slug ?? "magaza",
                SellerName: o.Seller?.Name ?? o.Store?.Name ?? "Yetkili Satıcı",
                SellerRating: o.Seller?.Rating ?? o.Store?.ReliabilityScore ?? 9.5,
                Price: o.Price,
                Url: o.Url,
                IsAvailable: o.IsAvailable,
                LastUpdated: o.LastCheckedAt.ToString("o"),
                ShippingText: o.ShippingText,
                GuaranteeText: o.GuaranteeText,
                InstallmentText: o.InstallmentText,
                IsOfficial: o.IsOfficial))
            .ToList();

        // Build Specs from Specifications collection
        var specsDict = p.Specifications
            .GroupBy(s => $"{s.Category}:{s.Key}")
            .ToDictionary(g => g.Key, g => g.First().Value);

        var display = new DisplaySpecsDto(
            Size: double.TryParse(specsDict.GetValueOrDefault("display:size", "6.7"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 6.7,
            Resolution: specsDict.GetValueOrDefault("display:resolution", "2796 x 1290"),
            Technology: specsDict.GetValueOrDefault("display:technology", "OLED"),
            RefreshRate: int.TryParse(specsDict.GetValueOrDefault("display:refreshRate", "120"), out var rr) ? rr : 120,
            Brightness: int.TryParse(specsDict.GetValueOrDefault("display:brightness", "2000"), out var br) ? br : 2000,
            AspectRatio: specsDict.GetValueOrDefault("display:aspectRatio", "19.5:9"),
            Protection: specsDict.GetValueOrDefault("display:protection", "Corning Gorilla Glass"),
            Hdr: specsDict.GetValueOrDefault("display:hdr", "HDR10+, Dolby Vision"),
            Ppi: int.TryParse(specsDict.GetValueOrDefault("display:ppi", "450"), out var ppi) ? ppi : 450);

        var mainMp = int.TryParse(specsDict.GetValueOrDefault("camera:mainMp", "48"), out var mmp) ? mmp : 48;
        var uwMp = int.TryParse(specsDict.GetValueOrDefault("camera:ultrawideMp", "12"), out var ump) ? ump : 12;
        var teleMp = int.TryParse(specsDict.GetValueOrDefault("camera:telephotoMp", "12"), out var tmp) ? tmp : 12;
        var frontMp = int.TryParse(specsDict.GetValueOrDefault("camera:frontMp", "12"), out var fmp) ? fmp : 12;

        var camera = new CameraSpecsDto(
            MainCamera: new MainCameraSpecsDto(
                mainMp,
                specsDict.GetValueOrDefault("camera:mainAperture", "f/1.8"),
                specsDict.GetValueOrDefault("camera:sensorSize", "1/1.3\""),
                bool.TryParse(specsDict.GetValueOrDefault("camera:ois", "true"), out var ois) ? ois : true,
                specsDict.GetValueOrDefault("camera:autofocus", "Dual Pixel PDAF")),
            Ultrawide: new UltrawideSpecsDto(
                uwMp,
                specsDict.GetValueOrDefault("camera:ultrawideAperture", "f/2.2"),
                120),
            Telephoto: teleMp > 0 ? new TelephotoSpecsDto(
                teleMp,
                specsDict.GetValueOrDefault("camera:telephotoAperture", "f/2.8"),
                int.TryParse(specsDict.GetValueOrDefault("camera:opticalZoom", "3"), out var oz) ? oz : 3,
                30) : null,
            FrontCamera: new FrontCameraSpecsDto(
                frontMp,
                specsDict.GetValueOrDefault("camera:frontAperture", "f/2.2"),
                true),
            VideoCapabilities: new List<string> { "4K@60fps", "4K@30fps", "1080p@240fps", "HDR Video" },
            Features: new List<string> { "Gece Modu", "Portre Modu", "Yapay Zeka Destekli Fotoğrafçılık", "Pro RAW Modu" });

        var battery = new BatterySpecsDto(
            Capacity: int.TryParse(specsDict.GetValueOrDefault("battery:capacity", "4500"), out var cap) ? cap : 4500,
            WiredCharging: int.TryParse(specsDict.GetValueOrDefault("battery:wiredCharging", "45"), out var wc) ? wc : 45,
            WirelessCharging: int.TryParse(specsDict.GetValueOrDefault("battery:wirelessCharging", "25"), out var wlc) ? wlc : 25,
            ReverseWirelessCharging: bool.TryParse(specsDict.GetValueOrDefault("battery:reverseWireless", "true"), out var rwc) ? rwc : true,
            ChargingTime: specsDict.GetValueOrDefault("battery:chargingTime", "0'dan %50'ye 30 dakikada"),
            Removable: false);

        var antutu = int.TryParse(specsDict.GetValueOrDefault("performance:antutuScore", "1450000"), out var ant) ? ant : 1450000;
        var gbSingle = int.TryParse(specsDict.GetValueOrDefault("performance:geekbenchSingle", "2100"), out var gbs) ? gbs : 2100;
        var gbMulti = int.TryParse(specsDict.GetValueOrDefault("performance:geekbenchMulti", "5600"), out var gbm) ? gbm : 5600;

        var performance = new PerformanceSpecsDto(
            Chipset: specsDict.GetValueOrDefault("performance:chipset", "Amiral Gemisi 8-Core İşlemci"),
            Cpu: specsDict.GetValueOrDefault("performance:cpu", "Octa-core yüksek performans mimarisi"),
            Gpu: specsDict.GetValueOrDefault("performance:gpu", "Gelişmiş Donanım Hızlandırmalı GPU"),
            Ram: primaryVariant?.RamGb ?? (int.TryParse(specsDict.GetValueOrDefault("performance:ram", "8"), out var rVal) ? rVal : 8),
            Storage: primaryVariant?.StorageGb ?? (int.TryParse(specsDict.GetValueOrDefault("performance:storage", "256"), out var sVal) ? sVal : 256),
            StorageType: specsDict.GetValueOrDefault("performance:storageType", "UFS 4.0"),
            ExpandableStorage: bool.TryParse(specsDict.GetValueOrDefault("performance:expandableStorage", "false"), out var exp) && exp,
            AntutuScore: antutu,
            GeekbenchSingle: gbSingle,
            GeekbenchMulti: gbMulti);

        var connectivity = new ConnectivitySpecsDto(
            Sim: specsDict.GetValueOrDefault("connectivity:sim", "Nano-SIM + eSIM"),
            Nfc: !bool.TryParse(specsDict.GetValueOrDefault("connectivity:nfc", "true"), out var nfc) || nfc,
            Bluetooth: specsDict.GetValueOrDefault("connectivity:bluetooth", "5.4"),
            Wifi: specsDict.GetValueOrDefault("connectivity:wifi", "Wi-Fi 7"),
            Usb: specsDict.GetValueOrDefault("connectivity:usb", "USB-C 3.2"),
            FiveG: !bool.TryParse(specsDict.GetValueOrDefault("connectivity:fiveG", "true"), out var fg) || fg,
            Infrared: bool.TryParse(specsDict.GetValueOrDefault("connectivity:infrared", "false"), out var ir) && ir);

        var design = new DesignSpecsDto(
            Width: double.TryParse(specsDict.GetValueOrDefault("design:width", "75.9"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dw) ? dw : 75.9,
            Height: double.TryParse(specsDict.GetValueOrDefault("design:height", "161.2"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dh) ? dh : 161.2,
            Thickness: double.TryParse(specsDict.GetValueOrDefault("design:thickness", "8.2"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dt) ? dt : 8.2,
            Weight: double.TryParse(specsDict.GetValueOrDefault("design:weight", "195"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dwg) ? dwg : 195,
            Materials: specsDict.GetValueOrDefault("design:materials", "Alüminyum Çerçeve ve Cam Gövde"),
            Colors: new List<string> { "Siyah", "Titanyum Gümüş", "Gece Mavisi" },
            WaterResistance: specsDict.GetValueOrDefault("design:waterResistance", "IP68"),
            Fingerprint: specsDict.GetValueOrDefault("design:fingerprint", "Ekran İçi Parmak İzi Okuyucu"),
            FaceId: !bool.TryParse(specsDict.GetValueOrDefault("design:faceId", "true"), out var fid) || fid);

        var software = new SoftwareSpecsDto(
            Os: specsDict.GetValueOrDefault("software:os", "Android 15 / iOS 18"),
            UpdatePolicy: specsDict.GetValueOrDefault("software:updatePolicy", "4-7 Yıl Resmi Güncelleme Garantisi"));

        var phoneSpecs = new PhoneSpecsDto(display, camera, battery, performance, connectivity, design, software);

        return new PhoneDto(
            Id: p.Id.ToString(),
            Slug: p.Slug,
            Name: p.Name,
            Brand: p.Brand.Name,
            BrandSlug: p.Brand.Slug,
            Image: p.ImageUrl,
            Images: new List<string> { p.ImageUrl, p.ImageUrl },
            LowestPrice: lowestPrice,
            HighestPrice: highestPrice,
            SixMonthLowestPrice: sixMonthLowest,
            PriceStore: lowestStore,
            PriceUrl: lowestUrl,
            ReleaseDate: p.ReleaseDate?.ToString("o") ?? DateTime.UtcNow.ToString("o"),
            IsNew: p.IsNew,
            IsPriceDropped: isPriceDropped,
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
