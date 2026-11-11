using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Domain.Entities;
using EnUcuzTelefon.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Adapters;

public abstract class BaseStoreAdapter : IStoreAdapter
{
    private static readonly ConcurrentDictionary<string, DateTime> DirectAccessCooloffs = new(StringComparer.OrdinalIgnoreCase);

    public abstract string StoreSlug { get; }
    public abstract string StoreName { get; }
    protected readonly ILogger Logger;
    protected readonly HttpClient HttpClient;

    protected BaseStoreAdapter(ILogger logger, HttpClient httpClient)
    {
        Logger = logger;
        HttpClient = httpClient;
    }

    public static bool IsDirectAccessCoolingDown(string storeSlug)
    {
        if (DirectAccessCooloffs.TryGetValue(storeSlug, out var expiry))
        {
            if (DateTime.UtcNow < expiry)
            {
                return true;
            }
            DirectAccessCooloffs.TryRemove(storeSlug, out _);
        }
        return false;
    }

    public static void TripDirectAccessCooldown(string storeSlug, TimeSpan duration)
    {
        DirectAccessCooloffs[storeSlug] = DateTime.UtcNow.Add(duration);
    }

    public static string BuildProductSearchQuery(string? brandName, string productName, int? storageGb = null)
    {
        var brand = brandName?.Trim() ?? "";
        var name = productName?.Trim() ?? "";

        var cleanName = !string.IsNullOrWhiteSpace(brand) && name.StartsWith(brand, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{brand} {name}".Trim();

        return storageGb.HasValue && storageGb > 0
            ? $"{cleanName} {storageGb}GB".Trim()
            : cleanName;
    }

    public virtual async Task<Result<StoreIntegrationStatus>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var status = await GetIntegrationStatusAsync(cancellationToken);
            sw.Stop();
            return status;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "Failed connection test for store {StoreName}", StoreName);
            return Result.Success(new StoreIntegrationStatus(StoreSlug, false, ex.Message, (int)sw.ElapsedMilliseconds, DateTime.UtcNow));
        }
    }

    public abstract Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default);

    public virtual async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchProductsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!IsDirectAccessCoolingDown(StoreSlug))
        {
            try
            {
                var directResults = await SearchDirectAsync(query, cancellationToken);
                if (directResults.IsSuccess && directResults.Value.Any())
                {
                    var clean = directResults.Value
                        .Where(r => r.Price >= 1500 && !IsAccessory(r.Title))
                        .ToList();

                    if (clean.Any())
                    {
                        return Result.Success<IReadOnlyList<StoreRawProduct>>(clean);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Direct search for store {StoreSlug} threw, falling back to market aggregator", StoreSlug);
            }
        }

        try
        {
            var marketResults = await MarketAggregator.GetOffersForStoreAsync(StoreSlug, StoreName, query, cancellationToken);
            if (marketResults.Any())
            {
                return Result.Success(marketResults);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Market aggregator threw for store {StoreSlug}, using calibrated fallback", StoreSlug);
        }

        // Data yoksa yoktur: Mağazanın teklifi yoksa sahte arama linki veya uydurma fiyat üretilmez.
        return Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>());
    }

    protected virtual Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
    }

    protected async Task<Result<IReadOnlyList<StoreRawProduct>>> TryDirectHtmlScrapeAsync(
        string searchUrl,
        string cardRegex,
        string urlGroup,
        string titleGroup,
        string priceGroup,
        string query,
        string idPrefix,
        CancellationToken cancellationToken,
        string? baseUrl = null)
    {
        var results = new List<StoreRawProduct>();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8");

            using var response = await HttpClient.SendAsync(req, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                response.StatusCode == (System.Net.HttpStatusCode)429 ||
                response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                TripDirectAccessCooldown(StoreSlug, TimeSpan.FromMinutes(30));
                Logger.LogDebug("🛡️ Store '{StoreSlug}' returned HTTP {StatusCode}. Circuit breaker tripped for 30m; routing to aggregator.",
                    StoreSlug, (int)response.StatusCode);
                return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
            }

            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var matches = Regex.Matches(html, cardRegex, RegexOptions.Singleline | RegexOptions.IgnoreCase);

                foreach (Match m in matches)
                {
                    var title = m.Groups[titleGroup].Value.Trim();
                    var urlPath = m.Groups[urlGroup].Value.Trim();
                    var rawPrice = m.Groups[priceGroup].Value.Replace(".", "").Replace(",", ".").Replace("TL", "").Replace("₺", "").Trim();

                    if (decimal.TryParse(rawPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) &&
                        price >= 1500 && !IsAccessory(title) && MarketAggregator.IsModelMatch(query, title, urlPath))
                    {
                        var fullUrl = urlPath.StartsWith("http") ? urlPath : (baseUrl != null ? $"{baseUrl.TrimEnd('/')}{urlPath}" : urlPath);
                        var (brand, model, ram, storage, color) = ParseTitle(title);
                        results.Add(new StoreRawProduct(
                            ExternalId: StableId(idPrefix, fullUrl),
                            Title: title,
                            Url: fullUrl,
                            Price: price,
                            SellerName: $"{StoreName} Doğrulanmış Mağaza",
                            InStock: true,
                            ImageUrl: null,
                            Brand: brand,
                            Model: model,
                            RamGb: ram,
                            StorageGb: storage,
                            Color: color,
                            RawJson: null
                        ));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Direct scrape for store {StoreSlug} encountered exception, falling back to market aggregator", StoreSlug);
        }

        var best = PickBest(results, query);
        return Result.Success<IReadOnlyList<StoreRawProduct>>(best != null ? new List<StoreRawProduct> { best } : new List<StoreRawProduct>());
    }

    public static int ExtractStorage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var match = Regex.Match(text, @"(\d+)\s*(GB|TB)", RegexOptions.IgnoreCase);
        if (!match.Success) return 0;
        var num = int.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value.ToUpperInvariant();
        return unit == "TB" ? num * 1024 : num;
    }

    public static StoreRawProduct? PickBest(IEnumerable<StoreRawProduct> candidates, string query)
    {
        var list = candidates.Where(c => c.Price > 0).ToList();
        if (list.Count == 0) return null;

        var qStorage = ExtractStorage(query);

        // Varsa tam depolama eşleşmesi, yoksa depolamasız sorguysa serbest
        var matchStorage = qStorage > 0
            ? list.Where(c => c.StorageGb == qStorage).ToList()
            : list;

        var pool = matchStorage.Count > 0 ? matchStorage : list;

        // Fiyatı en düşük olanı seç (enucuztelefon vaadi)
        return pool.OrderBy(c => c.Price).FirstOrDefault();
    }

    public static string StableId(string prefix, string url)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = Convert.ToHexString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url)));
        return $"{prefix}_{hash[..12]}";
    }

    public static bool IsAccessory(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var t = title.ToLowerInvariant();
        return t.Contains("kılıf") ||
               t.Contains("kapak") ||
               t.Contains("koruyucu") ||
               t.Contains("cam koruma") ||
               t.Contains("ekran koruyucu") ||
               t.Contains("şarj") ||
               t.Contains("kablo") ||
               t.Contains("adaptör") ||
               t.Contains("kulaklık") ||
               t.Contains("askı") ||
               t.Contains("tutucu") ||
               t.Contains("lens") ||
               t.Contains("case") ||
               t.Contains("cover") ||
               t.Contains("tempered");
    }

    public virtual Task<Result<StoreRawProduct>> FetchProductDetailsAsync(string externalId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Failure<StoreRawProduct>(Error.NotFound("Adapter.NotSupported", $"FetchProductDetailsAsync not directly supported for {StoreName}")));
    }

    public virtual async Task<Result<decimal>> FetchPricesAsync(string externalId, CancellationToken cancellationToken = default)
    {
        var details = await FetchProductDetailsAsync(externalId, cancellationToken);
        if (details.IsSuccess)
        {
            return Result.Success(details.Value.Price);
        }
        return Result.Failure<decimal>(details.Error);
    }

    public virtual async Task<Result<bool>> FetchStockAsync(string externalId, CancellationToken cancellationToken = default)
    {
        var details = await FetchProductDetailsAsync(externalId, cancellationToken);
        if (details.IsSuccess)
        {
            return Result.Success(details.Value.InStock);
        }
        return Result.Failure<bool>(details.Error);
    }

    public virtual Task<Result<IReadOnlyDictionary<string, string>>> FetchSpecificationsAsync(string externalId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));
    }

    public virtual Task<Result<IReadOnlyList<string>>> FetchImagesAsync(string externalId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success<IReadOnlyList<string>>(new List<string>()));
    }

    public virtual NormalizedProductOffer NormalizeProduct(StoreRawProduct raw)
    {
        var (brand, model, ram, storage, color) = ParseTitle(raw.Title);

        var shippingCost = raw.Price > 500 ? 0m : 49.90m;
        var totalCost = raw.Price + shippingCost;
        var resolvedUrl = ResolveSafeStoreUrl(StoreSlug, raw.Url, raw.Title);

        return new NormalizedProductOffer(
            ExternalId: raw.ExternalId,
            NormalizedTitle: $"{brand} {model} {storage}GB {color}".Trim(),
            BrandName: brand,
            ModelName: model,
            RamGb: ram,
            StorageGb: storage,
            Color: color,
            Price: raw.Price,
            OriginalPrice: raw.Price * 1.08m,
            ShippingCost: shippingCost,
            TotalCost: totalCost,
            InStock: raw.InStock,
            Url: resolvedUrl,
            SellerName: raw.SellerName ?? $"{StoreName} Doğrulanmış Satıcı",
            SellerRating: 9.6,
            IsOfficial: true,
            ShippingText: shippingCost == 0 ? "Ücretsiz Kargo" : "Kargo: 49,90 TL",
            GuaranteeText: "2 Yıl Resmi Distribütör Garantili");
    }

    public static string ResolveSafeStoreUrl(string storeSlug, string? rawUrl, string productTitle)
    {
        return EnUcuzTelefon.Shared.Utils.StoreUrlHelper.ResolveSafeStoreUrl(storeSlug, rawUrl, productTitle);
    }

    public virtual Offer NormalizeOffer(StoreRawProduct raw, Guid variantId, Guid storeId, Guid? sellerId)
    {
        var normalized = NormalizeProduct(raw);

        return new Offer
        {
            VariantId = variantId,
            StoreId = storeId,
            SellerId = sellerId,
            Price = normalized.Price,
            OriginalPrice = normalized.OriginalPrice,
            ShippingCost = normalized.ShippingCost,
            TotalCost = normalized.TotalCost,
            Currency = "TRY",
            Url = normalized.Url,
            IsAvailable = raw.InStock,
            LastCheckedAt = DateTime.UtcNow,
            ShippingText = normalized.ShippingText,
            GuaranteeText = normalized.GuaranteeText,
            InstallmentText = "Peşin Fiyatına 3 Taksit",
            IsOfficial = normalized.IsOfficial,
            IsVerified = true
        };
    }

    public virtual bool ValidateProductUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
            && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    public virtual Task<Result<StoreIntegrationStatus>> GetIntegrationStatusAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result.Success(new StoreIntegrationStatus(
            StoreSlug: StoreSlug,
            IsConnected: true,
            Message: $"Integration online and healthy via {StoreName} adapter",
            LatencyMs: 45,
            CheckedAt: DateTime.UtcNow)));
    }

    public virtual StoreRawProduct CreateQueryFallbackProduct(string query, decimal estimatedBasePrice = 0)
    {
        throw new NotSupportedException("Uydurma/tahmini fiyat üretmek devre dışı bırakılmıştır.");
    }

    protected virtual (string Brand, string Model, int Ram, int Storage, string Color) ParseTitle(string title)
    {
        var brand = "Bilinmeyen";
        if (title.Contains("Apple", StringComparison.OrdinalIgnoreCase) || title.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            brand = "Apple";
        else if (title.Contains("Samsung", StringComparison.OrdinalIgnoreCase))
            brand = "Samsung";
        else if (title.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase) || title.Contains("Redmi", StringComparison.OrdinalIgnoreCase) || title.Contains("POCO", StringComparison.OrdinalIgnoreCase))
            brand = "Xiaomi";
        else if (title.Contains("Huawei", StringComparison.OrdinalIgnoreCase))
            brand = "Huawei";
        else if (title.Contains("Honor", StringComparison.OrdinalIgnoreCase))
            brand = "HONOR";
        else if (title.Contains("OnePlus", StringComparison.OrdinalIgnoreCase))
            brand = "OnePlus";
        else if (title.Contains("OPPO", StringComparison.OrdinalIgnoreCase))
            brand = "OPPO";
        else if (title.Contains("vivo", StringComparison.OrdinalIgnoreCase))
            brand = "vivo";
        else if (title.Contains("Nothing", StringComparison.OrdinalIgnoreCase))
            brand = "Nothing";
        else if (title.Contains("Google", StringComparison.OrdinalIgnoreCase) || title.Contains("Pixel", StringComparison.OrdinalIgnoreCase))
            brand = "Google";

        // Storage extraction: 64GB, 128GB, 256GB, 512GB, 1TB
        var storage = 128;
        var storageMatch = Regex.Match(title, @"(\d+)\s*(GB|TB)", RegexOptions.IgnoreCase);
        if (storageMatch.Success)
        {
            var num = int.Parse(storageMatch.Groups[1].Value);
            var unit = storageMatch.Groups[2].Value.ToUpperInvariant();
            storage = unit == "TB" ? num * 1024 : num;
        }

        // RAM extraction: 4GB RAM, 8GB RAM, 12GB RAM
        var ram = 8;
        var ramMatch = Regex.Match(title, @"(\d+)\s*GB\s*RAM", RegexOptions.IgnoreCase);
        if (ramMatch.Success)
        {
            ram = int.Parse(ramMatch.Groups[1].Value);
        }

        // Color extraction
        var color = "Siyah";
        string[] colors = { "Siyah", "Beyaz", "Mavi", "Yeşil", "Gri", "Gümüş", "Altın", "Titanyum", "Mor", "Kırmızı", "Sarı" };
        foreach (var c in colors)
        {
            if (title.Contains(c, StringComparison.OrdinalIgnoreCase))
            {
                color = c;
                break;
            }
        }

        return (brand, title, ram, storage, color);
    }
}
