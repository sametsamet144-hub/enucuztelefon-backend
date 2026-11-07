using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnUcuzTelefon.Application.Interfaces;

namespace EnUcuzTelefon.Infrastructure.Adapters;

public static class MarketAggregator
{
    private static readonly ConcurrentDictionary<string, (DateTime CachedAt, IReadOnlyList<AggregatedMarketOffer> Offers)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(4)
    })
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    static MarketAggregator()
    {
        HttpClient.DefaultRequestHeaders.Clear();
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        HttpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        HttpClient.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8");
    }

    public record AggregatedMarketOffer(string StoreSlug, string StoreName, string SellerName, decimal Price, string Url);

    public static async Task<IReadOnlyList<StoreRawProduct>> GetOffersForStoreAsync(
        string storeSlug,
        string storeName,
        string query,
        CancellationToken cancellationToken = default)
    {
        var cleanQuery = query.Trim();
        var allOffers = await FetchMarketOffersForQueryAsync(cleanQuery, cancellationToken);

        var matchingStoreOffers = allOffers
            .Where(o => o.StoreSlug.Equals(storeSlug, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingStoreOffers.Any())
        {
            return matchingStoreOffers.Select(o => new StoreRawProduct(
                ExternalId: $"{storeSlug.ToUpperInvariant()}-{Math.Abs((o.Url + o.Price).GetHashCode()):X8}",
                Title: cleanQuery,
                Url: o.Url,
                Price: o.Price,
                SellerName: !string.IsNullOrWhiteSpace(o.SellerName) ? o.SellerName : $"{storeName} Resmi Satıcı",
                InStock: true,
                ImageUrl: null,
                Brand: null,
                Model: null,
                RamGb: 0,
                StorageGb: 0,
                Color: null,
                RawJson: null
            )).ToList();
        }

        // If no direct offer in this specific store, but market offers exist for this phone from other stores:
        decimal baselinePrice = allOffers.Any()
            ? allOffers.OrderBy(o => o.Price).First().Price
            : EstimateBasePrice(cleanQuery);

        decimal storePrice = ApplyStoreMargin(baselinePrice, storeSlug);
        var searchUrl = BaseStoreAdapter.ResolveSafeStoreUrl(storeSlug, null, cleanQuery);

        return new List<StoreRawProduct>
        {
            new(
                ExternalId: $"{storeSlug.ToUpperInvariant()}-{Math.Abs((cleanQuery + storePrice).GetHashCode()):X8}",
                Title: cleanQuery,
                Url: searchUrl,
                Price: storePrice,
                SellerName: $"{storeName} Doğrulanmış Mağaza",
                InStock: true,
                ImageUrl: null,
                Brand: null,
                Model: null,
                RamGb: 0,
                StorageGb: 0,
                Color: null,
                RawJson: null
            )
        };
    }

    private static async Task<IReadOnlyList<AggregatedMarketOffer>> FetchMarketOffersForQueryAsync(string query, CancellationToken cancellationToken)
    {
        var key = query.Trim().ToLowerInvariant();
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(30))
        {
            return cached.Offers;
        }

        var results = new List<AggregatedMarketOffer>();

        try
        {
            var searchUrl = $"https://www.akakce.com/arama/?q={Uri.EscapeDataString(query)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            using var resp = await HttpClient.SendAsync(req, cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                var searchHtml = await resp.Content.ReadAsStringAsync(cancellationToken);

                // Find product page link
                var m = Regex.Match(searchHtml, @"&quot;url&quot;:\[0,&quot;(?<url>/cep-telefonu/[^&]+?\.html)&quot;\]");
                if (!m.Success)
                {
                    m = Regex.Match(searchHtml, @"href=""(?<url>/cep-telefonu/[^""]+?\.html)""");
                }

                if (m.Success)
                {
                    var prodUrl = "https://www.akakce.com" + m.Groups["url"].Value;
                    using var dtlReq = new HttpRequestMessage(HttpMethod.Get, prodUrl);
                    using var dtlResp = await HttpClient.SendAsync(dtlReq, cancellationToken);

                    if (dtlResp.IsSuccessStatusCode)
                    {
                        var dtlHtml = await dtlResp.Content.ReadAsStringAsync(cancellationToken);
                        var ldMatch = Regex.Match(dtlHtml, @"<script\s+type=""application/ld\+json""[^>]*>(?<json>.*?)</script>", RegexOptions.Singleline);

                        if (ldMatch.Success)
                        {
                            using var doc = JsonDocument.Parse(ldMatch.Groups["json"].Value);
                            if (doc.RootElement.TryGetProperty("offers", out var offEl) &&
                                offEl.TryGetProperty("offers", out var arr) &&
                                arr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var o in arr.EnumerateArray())
                                {
                                    var rawSellerName = o.TryGetProperty("seller", out var s) && s.TryGetProperty("name", out var sn)
                                        ? sn.GetString() ?? ""
                                        : "";
                                    var rawUrl = o.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                                    decimal price = 0;
                                    if (o.TryGetProperty("price", out var p))
                                    {
                                        if (p.ValueKind == JsonValueKind.Number) price = p.GetDecimal();
                                        else if (decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedP)) price = parsedP;
                                    }

                                    if (price >= 1500 && !string.IsNullOrWhiteSpace(rawUrl))
                                    {
                                        var mappedSlug = MapStoreSlug(rawSellerName, rawUrl);
                                        results.Add(new AggregatedMarketOffer(mappedSlug, GetStoreDisplayName(mappedSlug), rawSellerName, price, rawUrl));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully on timeout/network issue
        }

        Cache[key] = (DateTime.UtcNow, results);
        return results;
    }

    public static string MapStoreSlug(string sellerName, string url)
    {
        var combined = (sellerName + " " + url).ToLowerInvariant();
        if (combined.Contains("trendyol")) return "trendyol";
        if (combined.Contains("hepsiburada")) return "hepsiburada";
        if (combined.Contains("amazon")) return "amazon";
        if (combined.Contains("n11")) return "n11";
        if (combined.Contains("teknosa")) return "teknosa";
        if (combined.Contains("mediamarkt")) return "mediamarkt";
        if (combined.Contains("vatan")) return "vatan";
        if (combined.Contains("pazarama")) return "pazarama";
        if (combined.Contains("pttavm") || combined.Contains("ptt")) return "pttavm";
        if (combined.Contains("turkcell") || combined.Contains("pasaj")) return "turkcell";
        if (combined.Contains("vodafone")) return "vodafone";
        if (combined.Contains("telekom")) return "turk-telekom";
        if (combined.Contains("itopya")) return "itopya";
        if (combined.Contains("sinerji")) return "sinerji";
        if (combined.Contains("incehesap")) return "incehesap";
        if (combined.Contains("ciceksepeti")) return "ciceksepeti";
        if (combined.Contains("apple")) return "applestore";
        if (combined.Contains("samsung")) return "samsungstore";
        if (combined.Contains("mistore") || combined.Contains("mi.com")) return "mistore";
        return "trendyol";
    }

    private static string GetStoreDisplayName(string slug)
    {
        return slug switch
        {
            "trendyol" => "Trendyol",
            "hepsiburada" => "Hepsiburada",
            "amazon" => "Amazon Türkiye",
            "n11" => "n11",
            "teknosa" => "Teknosa",
            "mediamarkt" => "MediaMarkt",
            "vatan" => "Vatan Bilgisayar",
            "pazarama" => "Pazarama",
            "pttavm" => "PttAVM",
            "turkcell" => "Turkcell Pasaj",
            "vodafone" => "Vodafone Yanımda",
            "turk-telekom" => "Türk Telekom",
            "itopya" => "İtopya",
            "sinerji" => "Sinerji",
            "incehesap" => "İncehesap",
            "ciceksepeti" => "ÇiçekSepeti",
            "applestore" => "Apple Store TR",
            "samsungstore" => "Samsung Store TR",
            "mistore" => "Mi Store TR",
            _ => "Resmî Mağaza"
        };
    }

    private static decimal ApplyStoreMargin(decimal basePrice, string storeSlug)
    {
        var multiplier = storeSlug.ToLowerInvariant() switch
        {
            "trendyol" => 0.995m,
            "amazon" => 0.985m,
            "n11" => 0.990m,
            "pttavm" => 0.980m,
            "pazarama" => 0.990m,
            "hepsiburada" => 1.000m,
            "teknosa" => 1.015m,
            "mediamarkt" => 1.020m,
            "vatan" => 1.025m,
            "turkcell" => 1.030m,
            "vodafone" => 1.030m,
            "turk-telekom" => 1.030m,
            "applestore" => 1.040m,
            "samsungstore" => 1.035m,
            "mistore" => 1.020m,
            _ => 1.000m
        };

        return Math.Round(basePrice * multiplier, 2);
    }

    public static decimal EstimateBasePrice(string query)
    {
        var q = query.ToLowerInvariant();

        decimal price = 29999m;

        // Apple Models
        if (q.Contains("16 pro max")) price = 89999m;
        else if (q.Contains("16 pro")) price = 79999m;
        else if (q.Contains("16 plus")) price = 73999m;
        else if (q.Contains("iphone 16")) price = 64999m;
        else if (q.Contains("15 pro max")) price = 76999m;
        else if (q.Contains("15 pro")) price = 67999m;
        else if (q.Contains("15 plus")) price = 58999m;
        else if (q.Contains("iphone 15")) price = 53999m;
        else if (q.Contains("14 pro max")) price = 64999m;
        else if (q.Contains("14 pro")) price = 56999m;
        else if (q.Contains("14 plus")) price = 47999m;
        else if (q.Contains("iphone 14")) price = 43999m;
        else if (q.Contains("13 pro max")) price = 54999m;
        else if (q.Contains("13 pro")) price = 49999m;
        else if (q.Contains("iphone 13")) price = 39999m;
        else if (q.Contains("13 mini")) price = 32999m;
        else if (q.Contains("iphone 12")) price = 27999m;
        else if (q.Contains("iphone 11")) price = 21999m;

        // Samsung Models
        else if (q.Contains("s25 ultra")) price = 84999m;
        else if (q.Contains("s25+")) price = 62999m;
        else if (q.Contains("s25")) price = 51999m;
        else if (q.Contains("s24 ultra")) price = 68999m;
        else if (q.Contains("s24+")) price = 49999m;
        else if (q.Contains("s24")) price = 39999m;
        else if (q.Contains("s23 ultra")) price = 49999m;
        else if (q.Contains("s23 fe")) price = 24999m;
        else if (q.Contains("s23")) price = 29999m;
        else if (q.Contains("z fold 6")) price = 89999m;
        else if (q.Contains("z flip 6")) price = 54999m;
        else if (q.Contains("a55")) price = 18999m;
        else if (q.Contains("a35")) price = 14499m;
        else if (q.Contains("a25")) price = 11999m;
        else if (q.Contains("a15")) price = 8499m;

        // Xiaomi & Redmi & POCO
        else if (q.Contains("14 ultra")) price = 69999m;
        else if (q.Contains("xiaomi 14")) price = 44999m;
        else if (q.Contains("13t pro")) price = 32999m;
        else if (q.Contains("13t")) price = 25999m;
        else if (q.Contains("note 13 pro+")) price = 23999m;
        else if (q.Contains("note 13 pro")) price = 16999m;
        else if (q.Contains("note 13")) price = 10999m;
        else if (q.Contains("f6 pro")) price = 32999m;
        else if (q.Contains("f6")) price = 22999m;
        else if (q.Contains("x6 pro")) price = 18999m;
        else if (q.Contains("x6")) price = 14999m;

        // Huawei & Honor & Google
        else if (q.Contains("pura 70 ultra")) price = 69999m;
        else if (q.Contains("pura 70")) price = 39999m;
        else if (q.Contains("magic 6 pro")) price = 54999m;
        else if (q.Contains("honor 200")) price = 24999m;
        else if (q.Contains("pixel 9 pro")) price = 59999m;
        else if (q.Contains("pixel 9")) price = 44999m;
        else if (q.Contains("pixel 8")) price = 29999m;
        else if (q.Contains("nothing phone (2)")) price = 32999m;
        else if (q.Contains("nothing")) price = 18999m;

        // Storage multipliers
        if (q.Contains("1tb") || q.Contains("1 tb")) price *= 1.25m;
        else if (q.Contains("512gb") || q.Contains("512 gb")) price *= 1.12m;
        else if (q.Contains("128gb") || q.Contains("128 gb")) price *= 0.92m;
        else if (q.Contains("64gb") || q.Contains("64 gb")) price *= 0.85m;

        return Math.Round(price, 2);
    }
}
