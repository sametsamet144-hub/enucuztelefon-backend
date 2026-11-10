using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Adapters;

public abstract class NoDataBrandStoreAdapter : BaseStoreAdapter
{
    protected NoDataBrandStoreAdapter(ILogger logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
}

public class TrendyolAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "trendyol";
    public override string StoreName => "Trendyol";

    public TrendyolAdapter(ILogger<TrendyolAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<StoreRawProduct>();
        try
        {
            var searchUrl = $"https://www.trendyol.com/sr?q={Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            req.Headers.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
            req.Headers.Add("sec-ch-ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");
            req.Headers.Add("sec-ch-ua-mobile", "?0");
            req.Headers.Add("sec-ch-ua-platform", "\"Windows\"");

            using var response = await HttpClient.SendAsync(req, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                response.StatusCode == (System.Net.HttpStatusCode)429 ||
                response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                TripDirectAccessCooldown(StoreSlug, TimeSpan.FromMinutes(30));
                Logger.LogWarning("🛡️ Store '{StoreSlug}' returned HTTP {StatusCode} (anti-bot protection). Tripping circuit breaker for 30m; routing queries to aggregator.",
                    StoreSlug, (int)response.StatusCode);
                return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
            }

            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var matchState = Regex.Match(html, @"window\.__SEARCH_APP_INITIAL_STATE__\s*=\s*(\{.*?\});\s*window\.__PRELOADED_STATE__", RegexOptions.Singleline);
                if (matchState.Success)
                {
                    using var doc = JsonDocument.Parse(matchState.Groups[1].Value);
                    if (doc.RootElement.TryGetProperty("products", out var prdArray) && prdArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in prdArray.EnumerateArray())
                        {
                            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var urlPath = p.TryGetProperty("url", out var u) ? u.GetString() : null;
                            decimal price = 0;
                            if (p.TryGetProperty("price", out var pObj))
                            {
                                if (pObj.TryGetProperty("discountedPrice", out var dp) && dp.ValueKind == JsonValueKind.Number)
                                    price = dp.GetDecimal();
                                else if (pObj.TryGetProperty("sellingPrice", out var sp) && sp.ValueKind == JsonValueKind.Number)
                                    price = sp.GetDecimal();
                            }
                            var fullUrl = urlPath != null ? (urlPath.StartsWith("http") ? urlPath : $"https://www.trendyol.com{urlPath}") : searchUrl;

                            if (!string.IsNullOrWhiteSpace(name) && price >= 1500 && !IsAccessory(name) && MarketAggregator.IsModelMatch(query, name, fullUrl))
                            {
                                var (brand, model, ram, storage, color) = ParseTitle(name);
                                results.Add(new StoreRawProduct(
                                    ExternalId: StableId("TY", fullUrl),
                                    Title: name,
                                    Url: fullUrl,
                                    Price: price,
                                    SellerName: "Trendyol Doğrulanmış Satıcı",
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
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to scrape Trendyol live query '{Query}'", query);
        }

        var best = PickBest(results, query);
        return Result.Success<IReadOnlyList<StoreRawProduct>>(best != null ? new List<StoreRawProduct> { best } : new List<StoreRawProduct>());
    }
}

public class HepsiburadaAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "hepsiburada";
    public override string StoreName => "Hepsiburada";

    public HepsiburadaAdapter(ILogger<HepsiburadaAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<StoreRawProduct>();
        try
        {
            var searchUrl = $"https://www.hepsiburada.com/ara?q={Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            req.Headers.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");

            using var response = await HttpClient.SendAsync(req, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                response.StatusCode == (System.Net.HttpStatusCode)429 ||
                response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                TripDirectAccessCooldown(StoreSlug, TimeSpan.FromMinutes(30));
                Logger.LogWarning("🛡️ Store '{StoreSlug}' returned HTTP {StatusCode} (anti-bot protection). Tripping circuit breaker for 30m; routing queries to aggregator.",
                    StoreSlug, (int)response.StatusCode);
                return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
            }

            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var matchNext = Regex.Match(html, @"<script id=""__NEXT_DATA__""[^>]*>(.*?)</script>", RegexOptions.Singleline);
                if (matchNext.Success)
                {
                    using var doc = JsonDocument.Parse(matchNext.Groups[1].Value);
                    if (doc.RootElement.TryGetProperty("props", out var props) &&
                        props.TryGetProperty("pageProps", out var pageProps) &&
                        pageProps.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("products", out var prdArray) &&
                        prdArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in prdArray.EnumerateArray())
                        {
                            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var urlPath = p.TryGetProperty("url", out var u) ? u.GetString() : null;
                            decimal price = 0;
                            if (p.TryGetProperty("price", out var pObj))
                            {
                                if (pObj.TryGetProperty("currentPrice", out var cp) && cp.ValueKind == JsonValueKind.Number)
                                    price = cp.GetDecimal();
                            }
                            var fullUrl = urlPath != null ? (urlPath.StartsWith("http") ? urlPath : $"https://www.hepsiburada.com{urlPath}") : searchUrl;

                            if (!string.IsNullOrWhiteSpace(name) && price >= 1500 && !IsAccessory(name) && MarketAggregator.IsModelMatch(query, name, fullUrl))
                            {
                                var (brand, model, ram, storage, color) = ParseTitle(name);
                                results.Add(new StoreRawProduct(
                                    ExternalId: StableId("HB", fullUrl),
                                    Title: name,
                                    Url: fullUrl,
                                    Price: price,
                                    SellerName: "Hepsiburada Resmi Satıcı",
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
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to scrape Hepsiburada live query '{Query}'", query);
        }

        var best = PickBest(results, query);
        return Result.Success<IReadOnlyList<StoreRawProduct>>(best != null ? new List<StoreRawProduct> { best } : new List<StoreRawProduct>());
    }
}

public class AmazonTurkeyAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "amazon";
    public override string StoreName => "Amazon Türkiye";

    public AmazonTurkeyAdapter(ILogger<AmazonTurkeyAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.amazon.com.tr/s?k={Uri.EscapeDataString(query)}";
        var pattern = @"<div[^>]*data-asin=""(?<asin>[A-Z0-9]{10})""[^>]*>.*?<h2[^>]*>.*?<a[^>]*href=""(?<url>[^""]+)""[^>]*>.*?<span[^>]*>(?<title>[^<]+)</span>.*?<span class=""a-price-whole"">(?<price>[\d.,]+)</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "AMZ", cancellationToken, "https://www.amazon.com.tr");
    }
}

public class TeknosaAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "teknosa";
    public override string StoreName => "Teknosa";

    public TeknosaAdapter(ILogger<TeknosaAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.teknosa.com/arama/?s={Uri.EscapeDataString(query)}";
        var pattern = @"<div[^>]*class=""prd-inner""[^>]*>.*?<a[^>]*class=""prd-link""[^>]*href=""(?<url>[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<span class=""prd-prc"">(?<price>[\d.,]+)\s*TL</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "TK", cancellationToken, "https://www.teknosa.com");
    }
}

public class MediaMarktAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "mediamarkt";
    public override string StoreName => "MediaMarkt";

    public MediaMarktAdapter(ILogger<MediaMarktAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var offers = await MarketAggregator.ScrapeMediaMarktLiveAsync(query, cancellationToken);
        var results = offers.Select(o =>
        {
            var title = !string.IsNullOrWhiteSpace(o.Title) ? o.Title : query;
            var (brand, model, ram, storage, color) = ParseTitle(title);
            var storageGb = o.StorageGb > 0 ? o.StorageGb : storage;
            return new StoreRawProduct(
                ExternalId: StableId("MM", o.Url),
                Title: title,
                Url: o.Url,
                Price: o.Price,
                SellerName: o.SellerName,
                InStock: true,
                ImageUrl: null,
                Brand: brand,
                Model: model,
                RamGb: ram,
                StorageGb: storageGb,
                Color: color,
                RawJson: null);
        }).ToList();

        var best = PickBest(results, query);
        return Result.Success<IReadOnlyList<StoreRawProduct>>(best != null ? new List<StoreRawProduct> { best } : new List<StoreRawProduct>());
    }
}

public class VatanAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "vatan";
    public override string StoreName => "Vatan Bilgisayar";

    public VatanAdapter(ILogger<VatanAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<StoreRawProduct>();
        try
        {
            var searchUrl = $"https://www.vatanbilgisayar.com/arama/{Uri.EscapeDataString(query)}/";
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
                Logger.LogWarning("🛡️ Store '{StoreSlug}' returned HTTP {StatusCode} (anti-bot protection). Tripping circuit breaker for 30m; routing queries to aggregator.",
                    StoreSlug, (int)response.StatusCode);
                return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
            }

            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var cardMatches = Regex.Matches(html, @"<div[^>]*class=""product-list__content""[^>]*>.*?<div class=""product-list__product-name""[^>]*>\s*<a[^>]*href=""(?<url>[^""]+)""[^>]*>\s*<h3>(?<name>[^<]+)</h3>.*?<span class=""product-list__price"">\s*(?<price>[\d.,]+)\s*</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                foreach (Match m in cardMatches)
                {
                    var title = m.Groups["name"].Value.Trim();
                    var urlPath = m.Groups["url"].Value.Trim();
                    var rawPrice = m.Groups["price"].Value.Replace(".", "").Replace(",", ".");

                    if (decimal.TryParse(rawPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) &&
                        price >= 1500 && !IsAccessory(title) && MarketAggregator.IsModelMatch(query, title, urlPath))
                    {
                        var fullUrl = urlPath.StartsWith("http") ? urlPath : $"https://www.vatanbilgisayar.com{urlPath}";
                        var (brand, model, ram, storage, color) = ParseTitle(title);
                        results.Add(new StoreRawProduct(
                            ExternalId: StableId("VATAN", fullUrl),
                            Title: title,
                            Url: fullUrl,
                            Price: price,
                            SellerName: "Vatan Bilgisayar Doğrulanmış Mağaza",
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
            Logger.LogWarning(ex, "Failed to scrape Vatan live query '{Query}'", query);
        }

        var best = PickBest(results, query);
        return Result.Success<IReadOnlyList<StoreRawProduct>>(best != null ? new List<StoreRawProduct> { best } : new List<StoreRawProduct>());
    }
}

public class N11Adapter : BaseStoreAdapter
{
    public override string StoreSlug => "n11";
    public override string StoreName => "n11";

    public N11Adapter(ILogger<N11Adapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<StoreRawProduct>();
        try
        {
            var searchUrl = $"https://www.n11.com/arama?q={Uri.EscapeDataString(query)}";
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
                Logger.LogWarning("🛡️ Store '{StoreSlug}' returned HTTP {StatusCode} (anti-bot protection). Tripping circuit breaker for 30m; routing queries to aggregator.",
                    StoreSlug, (int)response.StatusCode);
                return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
            }

            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);

                var jsonLdMatches = Regex.Matches(html, @"<script\s+type=[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match m in jsonLdMatches)
                {
                    try
                    {
                        var json = m.Groups[1].Value;
                        if (json.Contains("\"itemListElement\""))
                        {
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("itemListElement", out var listElem) && listElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var itemElem in listElem.EnumerateArray())
                                {
                                    if (itemElem.TryGetProperty("item", out var item))
                                    {
                                        var title = item.TryGetProperty("name", out var tProp) ? tProp.GetString() : null;
                                        var url = item.TryGetProperty("url", out var uProp) ? uProp.GetString() : null;
                                        var img = item.TryGetProperty("image", out var imgProp) ? imgProp.GetString() : null;
                                        decimal price = 0;
                                        if (item.TryGetProperty("offers", out var off) && off.TryGetProperty("price", out var pProp))
                                        {
                                            if (pProp.ValueKind == JsonValueKind.Number) price = pProp.GetDecimal();
                                            else if (decimal.TryParse(pProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) price = p;
                                        }

                                        if (!string.IsNullOrWhiteSpace(title) && price >= 1500 && !IsAccessory(title) && MarketAggregator.IsModelMatch(query, title, url))
                                        {
                                            var (brand, model, ram, storage, color) = ParseTitle(title);
                                            results.Add(new StoreRawProduct(
                                                ExternalId: StableId("N11", url ?? searchUrl),
                                                Title: title.Trim(),
                                                Url: url ?? searchUrl,
                                                Price: price,
                                                SellerName: "n11 Doğrulanmış Mağaza",
                                                InStock: true,
                                                ImageUrl: img,
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
                        }
                    }
                    catch { /* continue */ }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to scrape N11 live query '{Query}'", query);
        }

        var best = PickBest(results, query);
        return Result.Success<IReadOnlyList<StoreRawProduct>>(best != null ? new List<StoreRawProduct> { best } : new List<StoreRawProduct>());
    }
}

public class PttAvmAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "pttavm";
    public override string StoreName => "PttAVM";

    public PttAvmAdapter(ILogger<PttAvmAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.pttavm.com/arama?q={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*href=""(?<url>/[^""]+-p-\d+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<div class=""price-text"">(?<price>[\d.,]+)\s*TL</div>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "PTT", cancellationToken, "https://www.pttavm.com");
    }
}

public class TurkcellPasajAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "turkcell";
    public override string StoreName => "Turkcell (Turkcell Pasaj)";

    public TurkcellPasajAdapter(ILogger<TurkcellPasajAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://pasaj.turkcell.com.tr/arama?q={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*href=""(?<url>/pasaj/[^""]+)""[^>]*class=""m-p-card__link""[^>]*>.*?<div class=""m-p-card__title"">(?<title>[^<]+)</div>.*?<div class=""m-p-card__price"">(?<price>[\d.,]+)\s*TL</div>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "TKC", cancellationToken, "https://pasaj.turkcell.com.tr");
    }
}

public class VodafoneAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "vodafone";
    public override string StoreName => "Vodafone (Her Şey Yanımda)";

    public VodafoneAdapter(ILogger<VodafoneAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class TurkTelekomAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "turk-telekom";
    public override string StoreName => "Türk Telekom";

    public TurkTelekomAdapter(ILogger<TurkTelekomAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class CicekSepetiAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "ciceksepeti";
    public override string StoreName => "Çiçeksepeti";

    public CicekSepetiAdapter(ILogger<CicekSepetiAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.ciceksepeti.com/arama?query={Uri.EscapeDataString(query)}";
        var pattern = @"<div class=""products__item""[^>]*>.*?<a[^>]*href=""(?<url>[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<div class=""price__current"">(?<price>[\d.,]+)\s*TL</div>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "CS", cancellationToken, "https://www.ciceksepeti.com");
    }
}

public class AppleStoreAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "applestore";
    public override string StoreName => "Apple Store";
    public AppleStoreAdapter(ILogger<AppleStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class SamsungStoreAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "samsungstore";
    public override string StoreName => "Samsung Store";
    public SamsungStoreAdapter(ILogger<SamsungStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class XiaomiStoreAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "mistore";
    public override string StoreName => "Mi Store (Xiaomi)";
    public XiaomiStoreAdapter(ILogger<XiaomiStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class HuaweiStoreAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "huaweistore";
    public override string StoreName => "Huawei Store";
    public HuaweiStoreAdapter(ILogger<HuaweiStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class HonorStoreAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "honorstore";
    public override string StoreName => "Honor";
    public HonorStoreAdapter(ILogger<HonorStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class TecnoStoreAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "tecnostore";
    public override string StoreName => "Tecno";
    public TecnoStoreAdapter(ILogger<TecnoStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class InfinixStoreAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "infinixstore";
    public override string StoreName => "Infinix";
    public InfinixStoreAdapter(ILogger<InfinixStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class VivoStoreAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "vivostore";
    public override string StoreName => "Vivo";
    public VivoStoreAdapter(ILogger<VivoStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class EvkurAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "evkur";
    public override string StoreName => "Evkur";
    public EvkurAdapter(ILogger<EvkurAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class YonAvmAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "yonavm";
    public override string StoreName => "Yön AVM";
    public YonAvmAdapter(ILogger<YonAvmAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class MutluEvimAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "mutluevim";
    public override string StoreName => "Mutlu Evim";
    public MutluEvimAdapter(ILogger<MutluEvimAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class ArcelikAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "arcelik";
    public override string StoreName => "Arçelik";
    public ArcelikAdapter(ILogger<ArcelikAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class BekoAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "beko";
    public override string StoreName => "Beko";
    public BekoAdapter(ILogger<BekoAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class VestelAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "vestel";
    public override string StoreName => "Vestel";
    public VestelAdapter(ILogger<VestelAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class A101Adapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "a101";
    public override string StoreName => "A101";
    public A101Adapter(ILogger<A101Adapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class BimAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "bim";
    public override string StoreName => "BİM";
    public BimAdapter(ILogger<BimAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class SokAdapter : NoDataBrandStoreAdapter
{
    public override string StoreSlug => "sok";
    public override string StoreName => "ŞOK";
    public SokAdapter(ILogger<SokAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
}

public class StoreAdapterFactory : IStoreAdapterFactory
{
    private readonly IEnumerable<IStoreAdapter> _adapters;

    public StoreAdapterFactory(IEnumerable<IStoreAdapter> adapters)
    {
        _adapters = adapters;
    }

    public IStoreAdapter GetAdapter(string storeSlug)
    {
        var adapter = _adapters.FirstOrDefault(a => a.StoreSlug.Equals(storeSlug, StringComparison.OrdinalIgnoreCase));
        if (adapter == null)
        {
            throw new KeyNotFoundException($"No adapter registered for store '{storeSlug}'");
        }
        return adapter;
    }

    public IEnumerable<IStoreAdapter> GetAllAdapters() => _adapters;
}
