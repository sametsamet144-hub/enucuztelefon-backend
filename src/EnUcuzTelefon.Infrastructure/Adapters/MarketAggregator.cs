using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnUcuzTelefon.Application.Interfaces;

namespace EnUcuzTelefon.Infrastructure.Adapters;

public static class MarketAggregator
{
    public static readonly HashSet<string> AllowedStoreSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "hepsiburada", "trendyol", "amazon", "n11", "pttavm", "ciceksepeti", "mediamarkt", "teknosa", "vatan",
        "turkcell", "vodafone", "turk-telekom", "applestore", "samsungstore", "mistore", "huaweistore",
        "honorstore", "tecnostore", "infinixstore", "vivostore"
    };

    private static readonly ConcurrentDictionary<string, (DateTime CachedAt, IReadOnlyList<AggregatedMarketOffer> Offers)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly SemaphoreSlim AkakceRateLock = new(1, 1);
    private static DateTime _lastAkakceRequestUtc = DateTime.MinValue;

    private static readonly Lazy<Dictionary<string, SeedPhoneEntry>> SeedCatalog = new(LoadSeedCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Dictionary<string, string> KnownAkakceUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["iphone-17-pro-max"] = "/cep-telefonu/en-ucuz-iphone-17-pro-max-fiyati,370526315.html",
        ["samsung-galaxy-s25-ultra"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s25-ultra-512-gb-fiyati,917781807.html",
        ["samsung-galaxy-s26-ultra"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s26-ultra-fiyati,905288931.html",
        ["iphone-16-pro-max"] = "/cep-telefonu/en-ucuz-iphone-16-pro-max-256-gb-col-titanyum-fiyati,760887261.html",
        ["samsung-galaxy-s24-ultra"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s24-ultra-256-gb-titanyum-siyah-fiyati,507268191.html",
        ["huawei-pura-70-ultra"] = "/cep-telefonu/en-ucuz-huawei-pura-70-ultra-fiyati,640978983.html",
        ["vivo-x200-pro"] = "/cep-telefonu/en-ucuz-vivo-x200-pro-fiyati,692136834.html",
        ["honor-magic-6-pro"] = "/cep-telefonu/en-ucuz-honor-magic-6-pro-fiyati,334784133.html",
        ["iphone-16-pro"] = "/cep-telefonu/en-ucuz-iphone-16-pro-fiyati,760848471.html",
        ["iphone-16-plus"] = "/cep-telefonu/en-ucuz-iphone-16-plus-fiyati,310912012.html",
        ["iphone-16"] = "/cep-telefonu/en-ucuz-iphone-16-fiyati,233850107.html",
        ["iphone-15-pro-max"] = "/cep-telefonu/en-ucuz-iphone-15-pro-max-256-gb-mavi-titanyum-fiyati,282764370.html",
        ["iphone-15"] = "/cep-telefonu/en-ucuz-iphone-15-128-gb-mavi-fiyati,282674944.html",
        ["iphone-14"] = "/cep-telefonu/en-ucuz-iphone-14-128-gb-gece-yarisi-fiyati,2078289380.html",
        ["iphone-13"] = "/cep-telefonu/en-ucuz-iphone-13-fiyati,1101115595.html",
        ["iphone-air"] = "/cep-telefonu/en-ucuz-iphone-air-fiyati,732494413.html",
        ["samsung-galaxy-s25"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s25-fiyati,2095843206.html",
        ["samsung-galaxy-s25-phone-015"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s25-fiyati,2095843206.html",
        ["samsung-galaxy-s24-fe"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s24-fe-fiyati,614642333.html",
        ["samsung-galaxy-s23-fe"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s23-fe-256-gb-siyah-fiyati,313772219.html",
        ["samsung-galaxy-z-fold-6"] = "/cep-telefonu/en-ucuz-samsung-galaxy-z-fold-6-256-gb-fiyati,338129303.html",
        ["samsung-galaxy-s24-plus"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s24-plus-fiyati,176112818.html",
        ["samsung-galaxy-z-flip-6"] = "/cep-telefonu/en-ucuz-samsung-z-flip-6-256-gb-fiyati,488278224.html",
        ["samsung-galaxy-a56"] = "/cep-telefonu/en-ucuz-samsung-galaxy-a56-fiyati,786484511.html",
        ["samsung-galaxy-a55"] = "/cep-telefonu/en-ucuz-samsung-galaxy-a55-256-gb-fiyati,625151334.html",
        ["samsung-galaxy-a35"] = "/cep-telefonu/en-ucuz-samsung-galaxy-a35-fiyati,520986539.html",
        ["samsung-galaxy-a15"] = "/cep-telefonu/en-ucuz-samsung-galaxy-a15-fiyati,303631627.html",
        ["samsung-galaxy-a25-5g"] = "/cep-telefonu/en-ucuz-samsung-galaxy-a25-fiyati,162020298.html",
        ["xiaomi-15"] = "/cep-telefonu/en-ucuz-xiaomi-15-fiyati,594091693.html",
        ["xiaomi-14"] = "/cep-telefonu/en-ucuz-xiaomi-14-fiyati,581037957.html",
        ["xiaomi-14t-pro"] = "/cep-telefonu/en-ucuz-xiaomi-14t-pro-fiyati,773960099.html",
        ["xiaomi-13-ultra"] = "/cep-telefonu/en-ucuz-xiaomi-13-ultra-fiyati,245942919.html",
        ["poco-x6-pro"] = "/cep-telefonu/en-ucuz-poco-x6-pro-fiyati,362350710.html",
        ["poco-f6-pro"] = "/cep-telefonu/en-ucuz-poco-f6-512-gb-12-gb-fiyati,624555296.html",
        ["poco-m6-pro"] = "/cep-telefonu/en-ucuz-poco-m6-pro-4g-fiyati,488320849.html",
        ["honor-200-pro"] = "/cep-telefonu/en-ucuz-honor-200-fiyati,651949932.html",
        ["vivo-v30-5g"] = "/cep-telefonu/en-ucuz-vivo-v30-256-gb-siyah-fiyati,619377336.html",
        ["vivo-v40-5g"] = "/cep-telefonu/en-ucuz-vivo-v40-256-gb-12-gb-titanyum-grisi-fiyati,798168348.html",
        ["realme-gt-7-pro"] = "/cep-telefonu/en-ucuz-realme-gt-7-pro-fiyati,684475765.html",
        ["realme-gt-6"] = "/cep-telefonu/en-ucuz-realme-gt-6-fiyati,628604155.html",
        ["realme-12-pro-5g"] = "/cep-telefonu/en-ucuz-realme-12-pro-fiyati,469200959.html",
        ["redmi-note-14-pro"] = "/cep-telefonu/en-ucuz-xiaomi-redmi-note-14-pro-fiyati,692136487.html",
        ["redmi-note-13-pro-5g"] = "/cep-telefonu/en-ucuz-xiaomi-redmi-note-13-pro-plus-fiyati,252110494.html",
        ["redmi-note-13-pro-5g-phone-038"] = "/cep-telefonu/en-ucuz-xiaomi-redmi-note-13-pro-fiyati,252110247.html",
        ["redmi-note-13"] = "/cep-telefonu/en-ucuz-xiaomi-redmi-note-13-fiyati,252102941.html"
    };

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

    public record AggregatedMarketOffer(
        string StoreSlug, string StoreName, string SellerName, decimal Price, string Url,
        bool IsDirectProductUrl = false, string? Title = null, int StorageGb = 0);

    public static int? ExtractStorageGb(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int? best = null;
        foreach (Match m in Regex.Matches(text, @"(?<n>\d{1,4})\s*(?<u>gb|tb)\b", RegexOptions.IgnoreCase))
        {
            var n = int.Parse(m.Groups["n"].Value);
            var gb = m.Groups["u"].Value.Equals("tb", StringComparison.OrdinalIgnoreCase) ? n * 1024 : n;
            if (gb >= 32 && (best is null || gb > best)) best = gb;
        }

        return best;
    }

    private static string OfferKey(AggregatedMarketOffer o) => $"{o.StoreSlug}|{(o.StorageGb > 0 ? o.StorageGb.ToString() : "-")}";

    private record SeedPhoneEntry(string Slug, string Name, decimal LowestPrice, IReadOnlyList<AggregatedMarketOffer> Offers);

    public static bool IsSyntheticUrl(string? url)
    {
        return EnUcuzTelefon.Shared.Utils.StoreUrlHelper.IsSyntheticUrl(url);
    }

    public static bool IsDirectProductUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var u = url.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();

        // Reject non-phone supermarket / installment chains
        if (host.Contains("bim.com.tr") ||
            host.Contains("a101.com.tr") ||
            host.Contains("sokmarket.com.tr") ||
            host.Contains("kurumsalsok.com.tr") ||
            host.Contains("evkur.com.tr") ||
            host.Contains("yonavm.com.tr") ||
            host.Contains("mutluevim.com.tr") ||
            host.Contains("arcelik.com.tr") ||
            host.Contains("beko.com.tr") ||
            host.Contains("vestel.com.tr") ||
            host.Contains("myepoint.com") ||
            host.Contains("enucuztelefon.com"))
        {
            return false;
        }

        // Search or generic query / category / catalog fallback URLs
        if (u.Contains("/arama") ||
            u.Contains("/ara?") ||
            u.Contains("/ara/") ||
            u.Contains("/sr?q=") ||
            u.Contains("/sr?") ||
            u.Contains("/sr/") ||
            u.Contains("/s?k=") ||
            u.Contains("/s?") ||
            u.Contains("search_text=") ||
            u.Contains("search.html") ||
            u.Contains("?query=") ||
            u.Contains("&query=") ||
            u.Contains("?text=") ||
            u.Contains("&text=") ||
            u.Contains("?q=") ||
            u.Contains("&q=") ||
            u.Contains("/categories/") ||
            u.Contains("/kategori/") ||
            u.Contains("/katalog/") ||
            u.Contains("/kampanyalar/") ||
            u.Contains("/faturaya-ek") ||
            u.Contains("/cihazlar") ||
            u.Contains("/mobil/cep-telefonu") ||
            u.Contains("placeholder") ||
            u.Contains("-10000000") ||
            u.Contains("hbc0000100000"))
        {
            return false;
        }

        // Reject bare domain roots or generic brand pages
        if (path == "/" || string.IsNullOrWhiteSpace(path) || path == "/tr" || path == "/tr/")
        {
            return false;
        }
        if (u.EndsWith("apple.com/tr/iphone") || u.EndsWith("apple.com/tr/iphone/"))
        {
            return false;
        }
        if (u.EndsWith("shop.samsung.com/tr") || u.EndsWith("shop.samsung.com/tr/"))
        {
            return false;
        }
        if (u.Contains("mi.com/tr/phone"))
        {
            return false;
        }

        return true;
    }

    public static bool IsRefurbishedOrUsed(string? text, string? url = null)
    {
        var combined = $"{text} {url}".ToLowerInvariant();
        if (combined.Contains("yenilenmiş") ||
            combined.Contains("yenilenmis") ||
            combined.Contains("refurbished") ||
            combined.Contains("renewed") ||
            combined.Contains("open box") ||
            combined.Contains("açık kutu") ||
            combined.Contains("2. el") ||
            combined.Contains("2.el") ||
            combined.Contains("ikinci el") ||
            combined.Contains("ikinci-el") ||
            combined.Contains("kullanılmış") ||
            combined.Contains("sıfır ayarında") ||
            combined.Contains("hasarlı kutu") ||
            combined.Contains("teşhir") ||
            combined.Contains("teshir") ||
            combined.Contains("outlet"))
        {
            return true;
        }

        return Regex.IsMatch(combined, @"\b[abc]\s*-?\s*kalite\b");
    }

    private static readonly string[] ModelTierKeywords =
    {
        "redmi", "note", "lite", "mini", "fe", "se", "plus", "ultra", "max", "pro",
        "fold", "flip", "neo", "gt", "nova", "reno", "spark", "camon", "blade", "focus",
        "zenfone", "rog", "razr", "edge", "moto", "nord", "open", "xperia", "pixel", "pova", "narzo",
        "air", "turbo", "speed", "ace", "power", "prime", "play", "go"
    };

    private static readonly HashSet<string> IgnoredQueryTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "5g", "gb", "tb", "64", "128", "256", "512", "1024", "64gb", "128gb", "256gb", "512gb", "1024gb", "1tb",
        "apple", "tecno", "cep", "telefonu", "akıllı", "telefon"
    };

    public static bool IsModelMatch(string query, string? candidateTitle, string? candidateUrl = null)
    {
        if (string.IsNullOrWhiteSpace(candidateTitle)) return false;
        if (BaseStoreAdapter.IsAccessory(candidateTitle)) return false;
        if (IsRefurbishedOrUsed(candidateTitle, candidateUrl)) return false;

        var q = Regex.Replace(query.ToLowerInvariant().Replace('-', ' '), @"\(.*?\)", " ");
        q = Regex.Replace(q, @"\b(64|128|256|512|1024)\s*gb\b|\b1\s*tb\b", " ");
        q = Regex.Replace(q, @"\s+", " ").Trim();

        var c = candidateTitle.ToLowerInvariant().Replace("ui", "ul").Replace('-', ' ');
        c = Regex.Replace(c, @"\s+", " ").Trim();

        foreach (var tier in ModelTierKeywords)
        {
            var pattern = $@"\b{Regex.Escape(tier)}\b";
            var qHas = Regex.IsMatch(q, pattern) || (tier == "plus" && q.Contains('+'));
            var cHas = Regex.IsMatch(c, pattern) || (tier == "plus" && c.Contains('+'));
            if (qHas != cHas)
            {
                return false;
            }
        }

        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawToken in tokens)
        {
            var token = rawToken.TrimEnd('+');
            if (string.IsNullOrWhiteSpace(token) || IgnoredQueryTokens.Contains(token))
                continue;

            if (!Regex.IsMatch(c, $@"\b{Regex.Escape(token)}\b"))
            {
                return false;
            }
        }

        // Prevent iPhone SE (4. Nesil) from matching iPhone SE 2020 / SE 2 / SE 3
        if (Regex.IsMatch(q, @"\bse\b") && (c.Contains("2020") || c.Contains("2022") || c.Contains("se 2") || c.Contains("se 3")))
        {
            return false;
        }

        return true;
    }

    public static async Task<IReadOnlyList<StoreRawProduct>> GetOffersForStoreAsync(
        string storeSlug,
        string storeName,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedStoreSlugs.Contains(storeSlug)) return Array.Empty<StoreRawProduct>();

        var cleanQuery = query.Trim();
        var requestedStorage = ExtractStorageGb(cleanQuery);
        var allOffers = await FetchMarketOffersForQueryAsync(cleanQuery, null, cancellationToken);

        var matching = allOffers
            .Where(o => o.StoreSlug.Equals(storeSlug, StringComparison.OrdinalIgnoreCase))
            // Depolama istendiyse yalnızca AYNI depolamalı teklif. Bilinmeyen (seed / etiketsiz) teklif doğrulama
            // yolunda kullanılmaz: "veri yoksa yoktur".
            .Where(o => requestedStorage is null || o.StorageGb == requestedStorage)
            .Where(o => o.Title is null || IsModelMatch(StripStorageSuffix(cleanQuery), o.Title, o.Url))
            .OrderByDescending(o => o.IsDirectProductUrl)
            .ThenBy(o => o.Price)
            .ToList();

        return matching.Select(o => new StoreRawProduct(
            ExternalId: StableId(storeSlug, o.Url),
            Title: o.Title ?? cleanQuery,
            Url: o.Url,
            Price: o.Price,
            SellerName: !string.IsNullOrWhiteSpace(o.SellerName) ? o.SellerName : $"{storeName} Resmi Satıcı",
            InStock: true,
            ImageUrl: null,
            Brand: null,
            Model: null,
            RamGb: 0,
            StorageGb: o.StorageGb,
            Color: null,
            RawJson: null)).ToList();
    }

    private static string StableId(string storeSlug, string url)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url));
        return $"{storeSlug.ToUpperInvariant()}-{Convert.ToHexString(bytes)[..8]}";
    }

    public static async Task<IReadOnlyList<AggregatedMarketOffer>> FetchMarketOffersForQueryAsync(
        string query,
        string? knownSlug = null,
        CancellationToken cancellationToken = default)
    {
        var key = NormalizeLookupKey(query);
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.CachedAt < TimeSpan.FromMinutes(30))
        {
            return cached.Offers;
        }

        var offersByStore = new Dictionary<string, AggregatedMarketOffer>(StringComparer.OrdinalIgnoreCase);

        // 1. Seed catalog baseline offers (instant, pre-verified direct URLs and prices)
        var seedEntry = FindSeedEntry(query);
        if (seedEntry != null)
        {
            foreach (var seedOffer in seedEntry.Offers)
            {
                var k = OfferKey(seedOffer);
                if (!offersByStore.ContainsKey(k) ||
                    (seedOffer.IsDirectProductUrl && !offersByStore[k].IsDirectProductUrl))
                {
                    offersByStore[k] = seedOffer;
                }
            }
        }

        var cleanModelQuery = StripStorageSuffix(query);

        // 2. Live MediaMarkt Direct Scrape (fast, unblocked JSON-LD ItemList with direct .html URLs)
        try
        {
            var mmOffers = await ScrapeMediaMarktLiveAsync(cleanModelQuery, cancellationToken);
            foreach (var mo in mmOffers)
            {
                if (AllowedStoreSlugs.Contains(mo.StoreSlug))
                {
                    var k = OfferKey(mo);
                    offersByStore[k] = mo;
                }
            }
        }
        catch { /* graceful fallback */ }

        // 3. Live Akakçe Multi-Store Scrape (with rate-limiting semaphore so bursts never get blocked)
        bool complete = false;
        try
        {
            var effectiveSlug = !string.IsNullOrWhiteSpace(knownSlug) ? knownSlug : seedEntry?.Slug;
            var akakceOffers = await ScrapeAkakceLiveAsync(query, effectiveSlug, cancellationToken);
            complete = akakceOffers != null;
            foreach (var ao in akakceOffers ?? Array.Empty<AggregatedMarketOffer>())
            {
                if (!AllowedStoreSlugs.Contains(ao.StoreSlug))
                    continue;

                var k = OfferKey(ao);
                if (!offersByStore.TryGetValue(k, out var existing) ||
                    ao.IsDirectProductUrl ||
                    !existing.IsDirectProductUrl)
                {
                    // Preserve direct URL from seed/live if Akakce offer didn't resolve click redirect
                    var bestUrl = ao.IsDirectProductUrl
                        ? ao.Url
                        : (existing != null && existing.IsDirectProductUrl ? existing.Url : ao.Url);
                    var isDirect = IsDirectProductUrl(bestUrl);

                    offersByStore[k] = ao with
                    {
                        Url = bestUrl,
                        IsDirectProductUrl = isDirect
                    };
                }
            }
        }
        catch { /* graceful fallback */ }

        var results = offersByStore.Values
            .Where(o => AllowedStoreSlugs.Contains(o.StoreSlug) && o.Price >= 1500 && !string.IsNullOrWhiteSpace(o.Url))
            .OrderByDescending(o => o.IsDirectProductUrl)
            .ThenBy(o => o.Price)
            .ToList();

        if (results.Any() && complete)
        {
            Cache[key] = (DateTime.UtcNow, results);
        }

        return results;
    }

    public static async Task<IReadOnlyList<AggregatedMarketOffer>> ScrapeMediaMarktLiveAsync(string query, CancellationToken cancellationToken)
    {
        var list = new List<AggregatedMarketOffer>();
        var url = $"https://www.mediamarkt.com.tr/tr/search.html?query={Uri.EscapeDataString(query)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await HttpClient.SendAsync(req, cancellationToken);
        if (!resp.IsSuccessStatusCode) return list;

        var html = await resp.Content.ReadAsStringAsync(cancellationToken);
        var ldMatches = Regex.Matches(html, @"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match m in ldMatches)
        {
            var json = m.Groups[1].Value;
            if (!json.Contains("\"ItemList\"", StringComparison.Ordinal))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("itemListElement", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in arr.EnumerateArray())
                    {
                        if (!elem.TryGetProperty("item", out var item)) continue;
                        var name = item.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                        var prodUrl = item.TryGetProperty("url", out var uEl) ? uEl.GetString() : null;
                        decimal price = 0;
                        if (item.TryGetProperty("offers", out var offEl) && offEl.TryGetProperty("price", out var pEl))
                        {
                            if (pEl.ValueKind == JsonValueKind.Number) price = pEl.GetDecimal();
                            else if (decimal.TryParse(pEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) price = parsed;
                        }

                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(prodUrl) && price >= 3000 && IsModelMatch(query, name, prodUrl))
                        {
                            var storage = ExtractStorageGb(name) ?? BaseStoreAdapter.ExtractStorage(name);
                            list.Add(new AggregatedMarketOffer("mediamarkt", "MediaMarkt", "MediaMarkt Resmi Mağaza", Math.Round(price, 2), prodUrl, true, name, storage));
                        }
                    }
                }
            }
            catch { /* continue */ }
        }

        return list.GroupBy(o => o.StorageGb).Select(g => g.OrderBy(o => o.Price).First()).ToList();
    }

    private static async Task<IReadOnlyList<AggregatedMarketOffer>?> ScrapeAkakceLiveAsync(
        string query,
        string? knownSlug,
        CancellationToken cancellationToken)
    {
        var results = new List<AggregatedMarketOffer>();

        // Only wait briefly for Akakce rate lock; if busy, return null so caller knows scrape did not complete
        if (!await AkakceRateLock.WaitAsync(TimeSpan.FromMilliseconds(1200), cancellationToken))
        {
            return null;
        }

        try
        {
            var elapsed = DateTime.UtcNow - _lastAkakceRequestUtc;
            if (elapsed < TimeSpan.FromMilliseconds(450))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(450) - elapsed, cancellationToken);
            }

            string? detailPath = null;
            var slugCandidate = !string.IsNullOrWhiteSpace(knownSlug)
                ? knownSlug
                : Regex.Replace(query.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');

            if (KnownAkakceUrls.TryGetValue(slugCandidate, out var mappedPath))
            {
                detailPath = mappedPath;
            }
            else
            {
                detailPath = await TryFindAkakceUrlViaAutocompleteAsync(query, cancellationToken);
                if (string.IsNullOrWhiteSpace(detailPath))
                {
                    detailPath = await TryFindAkakceUrlViaSearchAsync(query, 1, cancellationToken);
                }
                if (string.IsNullOrWhiteSpace(detailPath))
                {
                    detailPath = await TryFindAkakceUrlViaSearchAsync(query, 2, cancellationToken);
                }
            }

            if (!string.IsNullOrWhiteSpace(detailPath))
            {
                var dtlUrl = $"https://www.akakce.com{detailPath}";
                _lastAkakceRequestUtc = DateTime.UtcNow;
                using var dtlReq = new HttpRequestMessage(HttpMethod.Get, dtlUrl);
                using var dtlResp = await HttpClient.SendAsync(dtlReq, cancellationToken);
                if (dtlResp.IsSuccessStatusCode)
                {
                    var dtlHtml = await dtlResp.Content.ReadAsStringAsync(cancellationToken);
                    var offerMatches = Regex.Matches(
                        dtlHtml,
                        @"&quot;price&quot;:\[0,(?<price>[\d.]+)\],&quot;pgName&quot;:\[0,&quot;(?<name>[^&]+)&quot;\],&quot;vdCode&quot;:\[0,(?<vd>\d+)\],&quot;vdName&quot;:\[0,&quot;(?<store>[^&]+)&quot;\].*?&quot;s&quot;:\[0,(?<s>\d+)\],&quot;v&quot;:\[0,(?<v>\d+)\],&quot;p&quot;:\[0,(?<p>\d+)\],&quot;c&quot;:\[0,(?<c>\d+)\],&quot;g&quot;:\[0,(?<g>\d+)\],&quot;f&quot;:\[0,&quot;(?<f>[^&]+)&quot;\]");

                    var candidates = new List<(string Slug, string Title, decimal Price, int? Storage, Match M)>();

                    foreach (Match mOffer in offerMatches)
                    {
                        var rawStore = WebUtility.HtmlDecode(mOffer.Groups["store"].Value).Trim();
                        var rawTitle = WebUtility.HtmlDecode(mOffer.Groups["name"].Value).Trim();

                        if (!decimal.TryParse(mOffer.Groups["price"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price < 3000)
                            continue;
                        if (IsRefurbishedOrUsed(rawTitle)) continue;

                        var mappedSlug = MapStoreSlug(rawStore, "");
                        if (string.IsNullOrWhiteSpace(mappedSlug) || !AllowedStoreSlugs.Contains(mappedSlug)) continue;

                        candidates.Add((mappedSlug, rawTitle, price, ExtractStorageGb(rawTitle), mOffer));
                    }

                    var best = candidates
                        .GroupBy(c => (c.Slug, c.Storage))
                        .Select(g => g.OrderBy(c => c.Price).First())
                        .OrderBy(c => c.Price)
                        .ToList();

                    int resolvedRedirects = 0;
                    foreach (var c in best)
                    {
                        string? directUrl = null;
                        if (resolvedRedirects < 12)
                        {
                            directUrl = await TryResolveAkakceDirectUrlAsync(
                                c.M.Groups["s"].Value,
                                c.M.Groups["v"].Value,
                                c.M.Groups["p"].Value,
                                c.M.Groups["c"].Value,
                                c.M.Groups["g"].Value,
                                c.M.Groups["f"].Value,
                                cancellationToken);

                            if (!string.IsNullOrWhiteSpace(directUrl))
                            {
                                resolvedRedirects++;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(directUrl))
                            continue;

                        var displayName = GetStoreDisplayName(c.Slug);
                        var storageGb = c.Storage ?? BaseStoreAdapter.ExtractStorage(c.Title);
                        results.Add(new AggregatedMarketOffer(
                            c.Slug,
                            displayName,
                            $"{displayName} Doğrulanmış Satıcı",
                            Math.Round(c.Price, 2),
                            directUrl,
                            true,
                            c.Title,
                            storageGb));
                    }
                }
            }
        }
        finally
        {
            AkakceRateLock.Release();
        }

        return results;
    }

    private static async Task<string?> TryFindAkakceUrlViaAutocompleteAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            var cleanSearch = StripStorageSuffix(query);
            var url = $"https://api6.akakce.com/autocomplete?q={Uri.EscapeDataString(cleanSearch)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Origin", "https://www.akakce.com");
            req.Headers.Add("Referer", "https://www.akakce.com/");
            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("result", out var resEl) &&
                resEl.TryGetProperty("groups", out var groupsEl) &&
                groupsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in groupsEl.EnumerateArray())
                {
                    if (g.TryGetProperty("title", out var tEl) && tEl.GetString() == "Ürünler" &&
                        g.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in itemsEl.EnumerateArray())
                        {
                            var text = it.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
                            if (it.TryGetProperty("action", out var actEl) &&
                                actEl.TryGetProperty("url", out var uEl))
                            {
                                var aUrl = uEl.GetString();
                                if (!string.IsNullOrWhiteSpace(aUrl) && aUrl.StartsWith("/cep-telefonu/") &&
                                    (string.IsNullOrWhiteSpace(text) || IsModelMatch(query, text)))
                                {
                                    return aUrl;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static async Task<string?> TryFindAkakceUrlViaSearchAsync(string query, int page, CancellationToken cancellationToken)
    {
        try
        {
            var cleanSearch = StripStorageSuffix(query);
            var searchUrl = page > 1
                ? $"https://www.akakce.com/arama/?q={Uri.EscapeDataString(cleanSearch)}&p={page}"
                : $"https://www.akakce.com/arama/?q={Uri.EscapeDataString(cleanSearch)}";

            _lastAkakceRequestUtc = DateTime.UtcNow;
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            var searchHtml = await resp.Content.ReadAsStringAsync(cancellationToken);
            var prodMatches = Regex.Matches(
                searchHtml,
                @"&quot;name&quot;:\[0,&quot;(?<name>[^&]+)&quot;\],&quot;hasLogo&quot;:\[0,[a-z]+\],&quot;imageUrl&quot;:\[0,&quot;[^&]*&quot;\],&quot;url&quot;:\[0,&quot;(?<url>/cep-telefonu/[^&]+?\.html)&quot;\]");

            foreach (Match pm in prodMatches)
            {
                var candidateName = WebUtility.HtmlDecode(pm.Groups["name"].Value);
                if (IsModelMatch(query, candidateName))
                {
                    return pm.Groups["url"].Value;
                }
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static async Task<string?> TryResolveAkakceDirectUrlAsync(
        string s, string v, string p, string c, string g, string f,
        CancellationToken cancellationToken)
    {
        try
        {
            var clickApi = $"https://api6.akakce.com/click?s={s}&v={v}&p={p}&c={c}&g={g}&f={Uri.EscapeDataString(f)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, clickApi);
            req.Headers.Add("Origin", "https://www.akakce.com");
            req.Headers.Add("Referer", "https://www.akakce.com/");

            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("url", out var urlEl)) return null;

            var redirUrl = urlEl.GetString();
            if (string.IsNullOrWhiteSpace(redirUrl)) return null;

            _lastAkakceRequestUtc = DateTime.UtcNow;
            using var redirReq = new HttpRequestMessage(HttpMethod.Get, redirUrl);
            using var redirResp = await HttpClient.SendAsync(redirReq, cancellationToken);
            if (!redirResp.IsSuccessStatusCode) return null;

            var html = await redirResp.Content.ReadAsStringAsync(cancellationToken);
            var m = Regex.Match(html, @"window\.location\.replace\('(?<u1>[^']+)'\)");
            if (!m.Success)
            {
                m = Regex.Match(html, @"<meta\s+http-equiv=""refresh""\s+content=""\d+;\s*url=(?<u1>[^"">]+)""", RegexOptions.IgnoreCase);
            }
            if (!m.Success)
            {
                m = Regex.Match(html, @"url=(?<u1>https?://[^""']+)");
            }

            if (m.Success)
            {
                var rawUrl = WebUtility.HtmlDecode(m.Groups["u1"].Value);
                var cleanUrl = CleanResolvedUrl(rawUrl);
                return cleanUrl;
            }
        }
        catch { /* ignore redirect failure */ }

        return null;
    }

    public static string CleanResolvedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;

        var query = uri.Query;
        if (string.IsNullOrEmpty(query)) return url;

        var trackingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term",
            "wt_pc", "adj_t", "adj_campaign", "adjust_tracker", "adjust_campaign",
            "mwebtoapp", "tag", "linkCode", "v", "vi", "vd", "yz", "sig", "exp"
        };

        var qParams = System.Web.HttpUtility.ParseQueryString(query);
        var filtered = new List<string>();
        foreach (string? key in qParams.AllKeys)
        {
            if (key != null && !trackingKeys.Contains(key))
            {
                var val = qParams[key];
                if (!string.IsNullOrEmpty(val))
                {
                    filtered.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(val)}");
                }
            }
        }

        var newQuery = filtered.Count > 0 ? "?" + string.Join("&", filtered) : "";
        return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}{newQuery}";
    }

    public static string MapStoreSlug(string sellerName, string url)
    {
        var combined = (sellerName + " " + url).ToLowerInvariant();
        if (combined.Contains("hepsiburada")) return "hepsiburada";
        if (combined.Contains("trendyol")) return "trendyol";
        if (combined.Contains("amazon")) return "amazon";
        if (combined.Contains("n11")) return "n11";
        if (combined.Contains("pttavm") || combined.Contains("ptt")) return "pttavm";
        if (combined.Contains("ciceksepeti") || combined.Contains("çiçeksepeti")) return "ciceksepeti";
        if (combined.Contains("mediamarkt")) return "mediamarkt";
        if (combined.Contains("teknosa")) return "teknosa";
        if (combined.Contains("vatan")) return "vatan";
        if (combined.Contains("turkcell") || combined.Contains("pasaj")) return "turkcell";
        if (combined.Contains("vodafone")) return "vodafone";
        if (combined.Contains("telekom")) return "turk-telekom";
        if (combined.Contains("apple.com") || combined.Contains("applestore")) return "applestore";
        if (combined.Contains("samsung.com") || combined.Contains("samsungstore")) return "samsungstore";
        if (combined.Contains("mistore") || combined.Contains("mi.com")) return "mistore";
        if (combined.Contains("huawei")) return "huaweistore";
        if (combined.Contains("honor")) return "honorstore";
        if (combined.Contains("tecno")) return "tecnostore";
        if (combined.Contains("infinix")) return "infinixstore";
        if (combined.Contains("vivo") || combined.Contains("myepoint")) return "vivostore";
        if (combined.Contains("evkur")) return "evkur";
        if (combined.Contains("yonavm") || combined.Contains("yön avm")) return "yonavm";
        if (combined.Contains("mutluevim") || combined.Contains("mutlu evim")) return "mutluevim";
        if (combined.Contains("arcelik") || combined.Contains("arçelik")) return "arcelik";
        if (combined.Contains("beko")) return "beko";
        if (combined.Contains("vestel")) return "vestel";
        if (combined.Contains("a101")) return "a101";
        if (combined.Contains("bim") || combined.Contains("bım")) return "bim";
        if (combined.Contains("sok") || combined.Contains("şok")) return "sok";
        return "";
    }

    public static string GetStoreDisplayName(string slug)
    {
        return slug.ToLowerInvariant() switch
        {
            "hepsiburada" => "Hepsiburada",
            "trendyol" => "Trendyol",
            "amazon" => "Amazon Türkiye",
            "n11" => "n11",
            "pttavm" => "PttAVM",
            "ciceksepeti" => "Çiçeksepeti",
            "mediamarkt" => "MediaMarkt",
            "teknosa" => "Teknosa",
            "vatan" => "Vatan Bilgisayar",
            "turkcell" => "Turkcell (Turkcell Pasaj)",
            "vodafone" => "Vodafone (Her Şey Yanımda)",
            "turk-telekom" => "Türk Telekom",
            "applestore" => "Apple Store",
            "samsungstore" => "Samsung Store",
            "mistore" => "Mi Store (Xiaomi)",
            "huaweistore" => "Huawei Store",
            "honorstore" => "Honor",
            "tecnostore" => "Tecno",
            "infinixstore" => "Infinix",
            "vivostore" => "Vivo",
            "evkur" => "Evkur",
            "yonavm" => "Yön AVM",
            "mutluevim" => "Mutlu Evim",
            "arcelik" => "Arçelik",
            "beko" => "Beko",
            "vestel" => "Vestel",
            "a101" => "A101",
            "bim" => "BİM",
            "sok" => "ŞOK",
            _ => slug
        };
    }

    private static string StripStorageSuffix(string query)
    {
        var clean = Regex.Replace(query, @"\b(64|128|256|512|1024)\s*GB\b|\b1\s*TB\b", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\b5G\b", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\(.*?\)", "");
        return Regex.Replace(clean, @"\s+", " ").Trim();
    }

    private static string NormalizeLookupKey(string text)
    {
        var clean = StripStorageSuffix(text).ToLowerInvariant();
        return Regex.Replace(clean, @"\s+", " ").Trim();
    }

    private static SeedPhoneEntry? FindSeedEntry(string query)
    {
        var map = SeedCatalog.Value;
        var norm = NormalizeLookupKey(query);
        if (map.TryGetValue(norm, out var entry))
            return entry;

        foreach (var kvp in map)
        {
            if (norm.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Contains(norm, StringComparison.OrdinalIgnoreCase))
            {
                if (IsModelMatch(query, kvp.Value.Name))
                {
                    return kvp.Value;
                }
            }
        }

        return null;
    }

    private static Dictionary<string, SeedPhoneEntry> LoadSeedCatalog()
    {
        var dict = new Dictionary<string, SeedPhoneEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Persistence", "SeedData", "catalog-seed.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Persistence", "SeedData", "catalog-seed.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "EnUcuzTelefon.Infrastructure", "Persistence", "SeedData", "catalog-seed.json")
            };

            var path = candidates.FirstOrDefault(File.Exists);
            if (path == null) return dict;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var slug = item.TryGetProperty("slug", out var sEl) ? sEl.GetString() ?? "" : "";
                var name = item.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                var brand = item.TryGetProperty("brand", out var bEl) ? bEl.GetString() ?? "" : "";
                var lowestPrice = item.TryGetProperty("lowestPrice", out var lpEl) && lpEl.ValueKind == JsonValueKind.Number
                    ? lpEl.GetDecimal()
                    : 0m;

                var offers = new List<AggregatedMarketOffer>();
                if (item.TryGetProperty("storesPrices", out var spArr) && spArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sp in spArr.EnumerateArray())
                    {
                        var stSlug = sp.TryGetProperty("storeSlug", out var ssEl) ? ssEl.GetString() ?? "" : "";
                        var stName = sp.TryGetProperty("store", out var stEl) ? stEl.GetString() ?? GetStoreDisplayName(stSlug) : GetStoreDisplayName(stSlug);
                        var seller = sp.TryGetProperty("sellerName", out var selEl) ? selEl.GetString() ?? stName : stName;
                        var p = sp.TryGetProperty("price", out var pEl) && pEl.ValueKind == JsonValueKind.Number ? pEl.GetDecimal() : 0m;
                        var u = sp.TryGetProperty("url", out var uEl) ? uEl.GetString() ?? "" : "";

                        if (!string.IsNullOrWhiteSpace(stSlug) && AllowedStoreSlugs.Contains(stSlug) && p > 0 && !string.IsNullOrWhiteSpace(u))
                        {
                            var isDirect = IsDirectProductUrl(u);
                            offers.Add(new AggregatedMarketOffer(stSlug, stName, seller, p, u, isDirect, name, ExtractStorageGb(name) ?? 0));
                        }
                    }
                }

                var entry = new SeedPhoneEntry(slug, name, lowestPrice, offers);
                var k1 = NormalizeLookupKey(name);
                var k2 = NormalizeLookupKey($"{brand} {name}");
                if (!string.IsNullOrWhiteSpace(k1)) dict[k1] = entry;
                if (!string.IsNullOrWhiteSpace(k2)) dict[k2] = entry;
                if (!string.IsNullOrWhiteSpace(slug)) dict[slug] = entry;
            }
        }
        catch { /* ignore seed read error */ }

        return dict;
    }
}
