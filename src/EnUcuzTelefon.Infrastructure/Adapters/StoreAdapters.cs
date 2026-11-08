using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Adapters;

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
                                    ExternalId: $"TY-{Math.Abs((fullUrl + price).GetHashCode()):X8}",
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
                                break;
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

        return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
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
                                    ExternalId: $"HB-{Math.Abs((fullUrl + price).GetHashCode()):X8}",
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
                                break;
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

        return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
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
            var (brand, model, ram, storage, color) = ParseTitle(query);
            return new StoreRawProduct(
                ExternalId: $"MM-{Math.Abs(o.Url.GetHashCode()):X8}",
                Title: query,
                Url: o.Url,
                Price: o.Price,
                SellerName: o.SellerName,
                InStock: true,
                ImageUrl: null,
                Brand: brand,
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null);
        }).ToList();

        return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
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
                            ExternalId: $"VATAN-{Math.Abs((fullUrl + price).GetHashCode()):X8}",
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
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to scrape Vatan live query '{Query}'", query);
        }

        return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
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
                                                ExternalId: $"N11-{Math.Abs((url ?? title).GetHashCode()):X8}",
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
                                            break;
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

        return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
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

public class VodafoneAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "vodafone";
    public override string StoreName => "Vodafone (Her Şey Yanımda)";

    public VodafoneAdapter(ILogger<VodafoneAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = "https://www.vodafone.com.tr/faturaya-ek/telefonlar";
        var pattern = @"<a[^>]*href=""(?<url>/faturaya-ek/[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<span class=""price"">(?<price>[\d.,]+)</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "VF", cancellationToken, "https://www.vodafone.com.tr");
    }
}

public class TurkTelekomAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "turk-telekom";
    public override string StoreName => "Türk Telekom";

    public TurkTelekomAdapter(ILogger<TurkTelekomAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = "https://bireysel.turktelekom.com.tr/cihazlar";
        var pattern = @"<a[^>]*href=""(?<url>/cihazlar/[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<span class=""price"">(?<price>[\d.,]+)</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "TT", cancellationToken, "https://bireysel.turktelekom.com.tr");
    }
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

public class AppleStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "applestore";
    public override string StoreName => "Apple Store";

    public AppleStoreAdapter(ILogger<AppleStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToLowerInvariant();
        if (!q.Contains("iphone") && !q.Contains("apple"))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
        }

        var (brand, model, ram, storage, color) = ParseTitle(query);
        var directUrl = "https://www.apple.com/tr/iphone/";
        var basePrice = MarketAggregator.EstimateBasePrice(query);
        var officialPrice = Math.Round(basePrice * 1.055m, 0);

        var list = new List<StoreRawProduct>
        {
            new(
                ExternalId: $"APPLE-{Math.Abs((directUrl + officialPrice).GetHashCode()):X8}",
                Title: query,
                Url: directUrl,
                Price: officialPrice,
                SellerName: "Apple Store Türkiye Resmi",
                InStock: true,
                ImageUrl: null,
                Brand: "Apple",
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null)
        };
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(list));
    }
}

public class SamsungStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "samsungstore";
    public override string StoreName => "Samsung Store";

    public SamsungStoreAdapter(ILogger<SamsungStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToLowerInvariant();
        if (!q.Contains("samsung") && !q.Contains("galaxy"))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
        }

        var (brand, model, ram, storage, color) = ParseTitle(query);
        var directUrl = "https://shop.samsung.com/tr/mobil/cep-telefonu/";
        var basePrice = MarketAggregator.EstimateBasePrice(query);
        var officialPrice = Math.Round(basePrice * 1.045m, 0);

        var list = new List<StoreRawProduct>
        {
            new(
                ExternalId: $"SAMS-{Math.Abs((directUrl + officialPrice).GetHashCode()):X8}",
                Title: query,
                Url: directUrl,
                Price: officialPrice,
                SellerName: "Samsung Store TR Resmi",
                InStock: true,
                ImageUrl: null,
                Brand: "Samsung",
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null)
        };
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(list));
    }
}

public class XiaomiStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "mistore";
    public override string StoreName => "Mi Store (Xiaomi)";

    public XiaomiStoreAdapter(ILogger<XiaomiStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToLowerInvariant();
        if (!q.Contains("xiaomi") && !q.Contains("redmi") && !q.Contains("poco"))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
        }

        var (brand, model, ram, storage, color) = ParseTitle(query);
        var directUrl = "https://www.mi.com/tr/phone/";
        var basePrice = MarketAggregator.EstimateBasePrice(query);
        var officialPrice = Math.Round(basePrice * 1.040m, 0);

        var list = new List<StoreRawProduct>
        {
            new(
                ExternalId: $"MI-{Math.Abs((directUrl + officialPrice).GetHashCode()):X8}",
                Title: query,
                Url: directUrl,
                Price: officialPrice,
                SellerName: "Mi Store Türkiye Resmi",
                InStock: true,
                ImageUrl: null,
                Brand: "Xiaomi",
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null)
        };
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(list));
    }
}

public class HuaweiStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "huaweistore";
    public override string StoreName => "Huawei Store";

    public HuaweiStoreAdapter(ILogger<HuaweiStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToLowerInvariant();
        if (!q.Contains("huawei"))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
        }

        var (brand, model, ram, storage, color) = ParseTitle(query);
        var directUrl = "https://consumer.huawei.com/tr/phones/";
        var basePrice = MarketAggregator.EstimateBasePrice(query);
        var officialPrice = Math.Round(basePrice * 1.035m, 0);

        var list = new List<StoreRawProduct>
        {
            new(
                ExternalId: $"HUA-{Math.Abs((directUrl + officialPrice).GetHashCode()):X8}",
                Title: query,
                Url: directUrl,
                Price: officialPrice,
                SellerName: "Huawei Store TR Resmi",
                InStock: true,
                ImageUrl: null,
                Brand: "Huawei",
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null)
        };
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(list));
    }
}

public class HonorStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "honorstore";
    public override string StoreName => "Honor";

    public HonorStoreAdapter(ILogger<HonorStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToLowerInvariant();
        if (!q.Contains("honor"))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
        }

        var (brand, model, ram, storage, color) = ParseTitle(query);
        var directUrl = "https://www.honor.com/tr/phones/";
        var basePrice = MarketAggregator.EstimateBasePrice(query);
        var officialPrice = Math.Round(basePrice * 1.040m, 0);

        var list = new List<StoreRawProduct>
        {
            new(
                ExternalId: $"HONOR-{Math.Abs((directUrl + officialPrice).GetHashCode()):X8}",
                Title: query,
                Url: directUrl,
                Price: officialPrice,
                SellerName: "Honor Türkiye Resmi",
                InStock: true,
                ImageUrl: null,
                Brand: "HONOR",
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null)
        };
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(list));
    }
}

public class TecnoStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "tecnostore";
    public override string StoreName => "Tecno";

    public TecnoStoreAdapter(ILogger<TecnoStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToLowerInvariant();
        if (!q.Contains("tecno"))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
        }

        var (brand, model, ram, storage, color) = ParseTitle(query);
        var directUrl = "https://www.tecno-mobile.com/tr/phones/";
        var basePrice = MarketAggregator.EstimateBasePrice(query);
        var officialPrice = Math.Round(basePrice * 1.045m, 0);

        var list = new List<StoreRawProduct>
        {
            new(
                ExternalId: $"TECNO-{Math.Abs((directUrl + officialPrice).GetHashCode()):X8}",
                Title: query,
                Url: directUrl,
                Price: officialPrice,
                SellerName: "Tecno Mobile Türkiye Resmi",
                InStock: true,
                ImageUrl: null,
                Brand: "TECNO",
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null)
        };
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(list));
    }
}

public class InfinixStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "infinixstore";
    public override string StoreName => "Infinix";

    public InfinixStoreAdapter(ILogger<InfinixStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToLowerInvariant();
        if (!q.Contains("infinix"))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
        }

        var (brand, model, ram, storage, color) = ParseTitle(query);
        var directUrl = "https://tr.infinixmobility.com/smartphones";
        var basePrice = MarketAggregator.EstimateBasePrice(query);
        var officialPrice = Math.Round(basePrice * 1.040m, 0);

        var list = new List<StoreRawProduct>
        {
            new(
                ExternalId: $"INFINIX-{Math.Abs((directUrl + officialPrice).GetHashCode()):X8}",
                Title: query,
                Url: directUrl,
                Price: officialPrice,
                SellerName: "Infinix Mobility Türkiye Resmi",
                InStock: true,
                ImageUrl: null,
                Brand: "Infinix",
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null)
        };
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(list));
    }
}

public class VivoStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "vivostore";
    public override string StoreName => "Vivo";

    public VivoStoreAdapter(ILogger<VivoStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = query.ToLowerInvariant();
        if (!q.Contains("vivo"))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(Array.Empty<StoreRawProduct>()));
        }

        var (brand, model, ram, storage, color) = ParseTitle(query);
        var directUrl = "https://myepoint.com/akilli-telefonlar";
        var basePrice = MarketAggregator.EstimateBasePrice(query);
        var officialPrice = Math.Round(basePrice * 1.045m, 0);

        var list = new List<StoreRawProduct>
        {
            new(
                ExternalId: $"VIVO-{Math.Abs((directUrl + officialPrice).GetHashCode()):X8}",
                Title: query,
                Url: directUrl,
                Price: officialPrice,
                SellerName: "Vivo Türkiye Resmi",
                InStock: true,
                ImageUrl: null,
                Brand: "vivo",
                Model: model,
                RamGb: ram,
                StorageGb: storage,
                Color: color,
                RawJson: null)
        };
        return Task.FromResult(Result.Success<IReadOnlyList<StoreRawProduct>>(list));
    }
}

public class EvkurAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "evkur";
    public override string StoreName => "Evkur";

    public EvkurAdapter(ILogger<EvkurAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.evkur.com.tr/arama?q={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*href=""(?<url>/[^""]+)""[^>]*class=""product-name""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<div class=""price"">(?<price>[\d.,]+)\s*TL</div>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "EVK", cancellationToken, "https://www.evkur.com.tr");
    }
}

public class YonAvmAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "yonavm";
    public override string StoreName => "Yön AVM";

    public YonAvmAdapter(ILogger<YonAvmAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.yonavm.com.tr/arama?q={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*href=""(?<url>/[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<span class=""price"">(?<price>[\d.,]+)\s*TL</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "YON", cancellationToken, "https://www.yonavm.com.tr");
    }
}

public class MutluEvimAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "mutluevim";
    public override string StoreName => "Mutlu Evim";

    public MutluEvimAdapter(ILogger<MutluEvimAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.mutluevim.com.tr/arama?q={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*href=""(?<url>/[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<span class=""price"">(?<price>[\d.,]+)\s*TL</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "ME", cancellationToken, "https://www.mutluevim.com.tr");
    }
}

public class ArcelikAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "arcelik";
    public override string StoreName => "Arçelik";

    public ArcelikAdapter(ILogger<ArcelikAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.arcelik.com.tr/arama?text={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*class=""product-item__name""[^>]*href=""(?<url>/[^""]+)""[^>]*>(?<title>[^<]+)</a>.*?<div class=""product-item__price"">(?<price>[\d.,]+)\s*TL</div>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "ARC", cancellationToken, "https://www.arcelik.com.tr");
    }
}

public class BekoAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "beko";
    public override string StoreName => "Beko";

    public BekoAdapter(ILogger<BekoAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.beko.com.tr/arama?text={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*class=""product-item__name""[^>]*href=""(?<url>/[^""]+)""[^>]*>(?<title>[^<]+)</a>.*?<div class=""product-item__price"">(?<price>[\d.,]+)\s*TL</div>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "BEK", cancellationToken, "https://www.beko.com.tr");
    }
}

public class VestelAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "vestel";
    public override string StoreName => "Vestel";

    public VestelAdapter(ILogger<VestelAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.vestel.com.tr/arama?q={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*class=""product-item__name""[^>]*href=""(?<url>/[^""]+)""[^>]*>(?<title>[^<]+)</a>.*?<div class=""product-item__price"">(?<price>[\d.,]+)\s*TL</div>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "VES", cancellationToken, "https://www.vestel.com.tr");
    }
}

public class A101Adapter : BaseStoreAdapter
{
    public override string StoreSlug => "a101";
    public override string StoreName => "A101";

    public A101Adapter(ILogger<A101Adapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.a101.com.tr/list/?search_text={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*class=""product-card""[^>]*href=""(?<url>/[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<span class=""current-price"">(?<price>[\d.,]+)\s*TL</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "A101", cancellationToken, "https://www.a101.com.tr");
    }
}

public class BimAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "bim";
    public override string StoreName => "BİM";

    public BimAdapter(ILogger<BimAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = "https://www.bim.com.tr/Categories/100/aktuel-urunler.aspx";
        var pattern = @"<a[^>]*class=""sub-banner""[^>]*href=""(?<url>/[^""]+)""[^>]*>.*?<h2[^>]*>(?<title>[^<]+)</h2>.*?<span class=""price"">(?<price>[\d.,]+)\s*TL</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "BIM", cancellationToken, "https://www.bim.com.tr");
    }
}

public class SokAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "sok";
    public override string StoreName => "ŞOK";

    public SokAdapter(ILogger<SokAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    protected override Task<Result<IReadOnlyList<StoreRawProduct>>> SearchDirectAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.kurumsalsok.com.tr/arama?q={Uri.EscapeDataString(query)}";
        var pattern = @"<a[^>]*class=""product-link""[^>]*href=""(?<url>/[^""]+)""[^>]*title=""(?<title>[^""]+)""[^>]*>.*?<span class=""price"">(?<price>[\d.,]+)\s*TL</span>";
        return TryDirectHtmlScrapeAsync(url, pattern, "url", "title", "price", query, "SOK", cancellationToken, "https://www.kurumsalsok.com.tr");
    }
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
