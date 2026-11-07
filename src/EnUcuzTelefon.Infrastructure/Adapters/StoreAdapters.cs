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

    public override async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchProductsAsync(string query, CancellationToken cancellationToken = default)
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
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var matchState = Regex.Match(html, @"window\.__SEARCH_APP_INITIAL_STATE__\s*=\s*(\{.*?\});\s*window\.__PRELOADED_STATE__", RegexOptions.Singleline);
                if (matchState.Success)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(matchState.Groups[1].Value);
                    if (doc.RootElement.TryGetProperty("products", out var prdArray) && prdArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var p in prdArray.EnumerateArray())
                        {
                            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var urlPath = p.TryGetProperty("url", out var u) ? u.GetString() : null;
                            decimal price = 0;
                            if (p.TryGetProperty("price", out var pObj))
                            {
                                if (pObj.TryGetProperty("discountedPrice", out var dp) && dp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                    price = dp.GetDecimal();
                                else if (pObj.TryGetProperty("sellingPrice", out var sp) && sp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                    price = sp.GetDecimal();
                            }
                            var fullUrl = urlPath != null ? (urlPath.StartsWith("http") ? urlPath : $"https://www.trendyol.com{urlPath}") : searchUrl;

                            if (!string.IsNullOrWhiteSpace(name) && price > 0)
                            {
                                var (brand, model, ram, storage, color) = ParseTitle(name);
                                results.Add(new StoreRawProduct(
                                    ExternalId: $"TY-{Math.Abs(name.GetHashCode()):X8}",
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

    public override async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchProductsAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<StoreRawProduct>();
        try
        {
            var searchUrl = $"https://www.hepsiburada.com/ara?q={Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
            req.Headers.Add("sec-ch-ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");

            using var response = await HttpClient.SendAsync(req, cancellationToken);
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
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("itemListElement", out var listElem) && listElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var itemElem in listElem.EnumerateArray())
                                {
                                    if (itemElem.TryGetProperty("item", out var item))
                                    {
                                        var title = item.TryGetProperty("name", out var t) ? t.GetString() : null;
                                        var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                                        decimal price = 0;
                                        if (item.TryGetProperty("offers", out var off) && off.TryGetProperty("price", out var pProp))
                                        {
                                            if (pProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                                price = pProp.GetDecimal();
                                            else if (decimal.TryParse(pProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p))
                                                price = p;
                                        }

                                        if (!string.IsNullOrWhiteSpace(title) && price > 0)
                                        {
                                            var (brand, model, ram, storage, color) = ParseTitle(title);
                                            results.Add(new StoreRawProduct(
                                                ExternalId: $"HB-{Math.Abs(title.GetHashCode()):X8}",
                                                Title: title.Trim(),
                                                Url: url ?? searchUrl,
                                                Price: price,
                                                SellerName: "Hepsiburada Resmi",
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
                    catch { /* continue */ }
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
}

public class TeknosaAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "teknosa";
    public override string StoreName => "Teknosa";
    public TeknosaAdapter(ILogger<TeknosaAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class MediaMarktAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "mediamarkt";
    public override string StoreName => "MediaMarkt";
    public MediaMarktAdapter(ILogger<MediaMarktAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class VatanAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "vatan";
    public override string StoreName => "Vatan Bilgisayar";
    public VatanAdapter(ILogger<VatanAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class N11Adapter : BaseStoreAdapter
{
    public override string StoreSlug => "n11";
    public override string StoreName => "n11";

    public N11Adapter(ILogger<N11Adapter> logger, HttpClient httpClient) : base(logger, httpClient) { }

    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);

    public override async Task<Result<IReadOnlyList<StoreRawProduct>>> SearchProductsAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<StoreRawProduct>();
        try
        {
            var searchUrl = $"https://www.n11.com/arama?q={Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");

            using var response = await HttpClient.SendAsync(req, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);

                // 1. Check for schema.org ItemList JSON-LD
                var jsonLdMatches = Regex.Matches(html, @"<script\s+type=[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match m in jsonLdMatches)
                {
                    try
                    {
                        var json = m.Groups[1].Value;
                        if (json.Contains("\"@type\":\"ItemList\"") || json.Contains("\"itemListElement\""))
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("itemListElement", out var listElem) && listElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                foreach (var itemElem in listElem.EnumerateArray())
                                {
                                    if (itemElem.TryGetProperty("item", out var item))
                                    {
                                        var title = item.TryGetProperty("name", out var tProp) ? tProp.GetString() : null;
                                        var url = item.TryGetProperty("url", out var uProp) ? uProp.GetString() : null;
                                        var img = item.TryGetProperty("image", out var imgProp) ? imgProp.GetString() : null;
                                        decimal price = 0;
                                        if (item.TryGetProperty("offers", out var off))
                                        {
                                            if (off.TryGetProperty("price", out var pProp))
                                            {
                                                if (pProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                                                    price = pProp.GetDecimal();
                                                else if (decimal.TryParse(pProp.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p))
                                                    price = p;
                                            }
                                        }

                                        if (!string.IsNullOrWhiteSpace(title) && price > 0)
                                        {
                                            var (brand, model, ram, storage, color) = ParseTitle(title);
                                            var extId = $"N11-{Math.Abs(title.GetHashCode()):X8}";
                                            results.Add(new StoreRawProduct(
                                                ExternalId: extId,
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

                // 2. Check HTML search items if JSON-LD didn't yield items
                if (!results.Any())
                {
                    var productCardPattern = new Regex(@"<li[^>]*class=[""'][^""']*column[^""']*[""'][^>]*>.*?<h3[^>]*class=[""']productName[""'][^>]*>(?<title>[^<]+)</h3>.*?<a[^>]*class=[""']plink[""'][^>]*href=[""'](?<url>[^""']+)[""'].*?<ins>(?<price>[\d.,]+)\s*TL", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    var matches = productCardPattern.Matches(html);
                    foreach (Match pm in matches)
                    {
                        var title = pm.Groups["title"].Value.Trim();
                        var url = pm.Groups["url"].Value.Trim();
                        var rawPrice = pm.Groups["price"].Value.Replace(".", "").Replace(",", ".");
                        if (decimal.TryParse(rawPrice, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var prc) && prc > 0)
                        {
                            var (brand, model, ram, storage, color) = ParseTitle(title);
                            var extId = $"N11-{Math.Abs(title.GetHashCode()):X8}";
                            results.Add(new StoreRawProduct(
                                ExternalId: extId,
                                Title: title,
                                Url: url,
                                Price: prc,
                                SellerName: "n11 Yetkili Satıcı",
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
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to scrape N11 live query '{Query}'", query);
        }

        return Result.Success<IReadOnlyList<StoreRawProduct>>(results);
    }
}

public class PazaramaAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "pazarama";
    public override string StoreName => "Pazarama";
    public PazaramaAdapter(ILogger<PazaramaAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class PttAvmAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "pttavm";
    public override string StoreName => "PttAVM";
    public PttAvmAdapter(ILogger<PttAvmAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class TurkcellPasajAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "turkcell";
    public override string StoreName => "Turkcell Pasaj";
    public TurkcellPasajAdapter(ILogger<TurkcellPasajAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class VodafoneAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "vodafone";
    public override string StoreName => "Vodafone Yanımda";
    public VodafoneAdapter(ILogger<VodafoneAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class TurkTelekomAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "turk-telekom";
    public override string StoreName => "Türk Telekom";
    public TurkTelekomAdapter(ILogger<TurkTelekomAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class ItopyaAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "itopya";
    public override string StoreName => "İtopya";
    public ItopyaAdapter(ILogger<ItopyaAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class SinerjiAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "sinerji";
    public override string StoreName => "Sinerji";
    public SinerjiAdapter(ILogger<SinerjiAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class IncehesapAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "incehesap";
    public override string StoreName => "İncehesap";
    public IncehesapAdapter(ILogger<IncehesapAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class CicekSepetiAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "ciceksepeti";
    public override string StoreName => "ÇiçekSepeti";
    public CicekSepetiAdapter(ILogger<CicekSepetiAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class AppleStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "applestore";
    public override string StoreName => "Apple Store TR";
    public AppleStoreAdapter(ILogger<AppleStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class SamsungStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "samsungstore";
    public override string StoreName => "Samsung Store TR";
    public SamsungStoreAdapter(ILogger<SamsungStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
}

public class XiaomiStoreAdapter : BaseStoreAdapter
{
    public override string StoreSlug => "mistore";
    public override string StoreName => "Mi Store TR";
    public XiaomiStoreAdapter(ILogger<XiaomiStoreAdapter> logger, HttpClient httpClient) : base(logger, httpClient) { }
    public override Task<Result<IReadOnlyList<StoreRawProduct>>> FetchProductsAsync(string? category = null, CancellationToken cancellationToken = default)
        => SearchProductsAsync("cep telefonu", cancellationToken);
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
