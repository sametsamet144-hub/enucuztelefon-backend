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
        "honorstore", "tecnostore", "infinixstore", "vivostore", "evkur", "yonavm", "mutluevim",
        "arcelik", "beko", "vestel", "a101", "bim", "sok"
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
        ["samsung-galaxy-s25"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s25-plus-fiyati,917798760.html",
        ["samsung-galaxy-s25-phone-015"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s25-fiyati,2095843206.html",
        ["samsung-galaxy-s24-fe"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s24-fe-fiyati,614642333.html",
        ["samsung-galaxy-s23-fe"] = "/cep-telefonu/en-ucuz-samsung-galaxy-s23-fe-256-gb-siyah-fiyati,313772219.html",
        ["samsung-galaxy-z-fold-6"] = "/cep-telefonu/en-ucuz-samsung-galaxy-z-fold-6-256-gb-fiyati,338129303.html",
        ["samsung-galaxy-z-flip-6"] = "/cep-telefonu/en-ucuz-samsung-z-flip-6-256-gb-fiyati,488278224.html",
        ["samsung-galaxy-a56"] = "/cep-telefonu/en-ucuz-samsung-galaxy-a56-fiyati,786484511.html",
        ["xiaomi-15"] = "/cep-telefonu/en-ucuz-xiaomi-15-fiyati,594091693.html"
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

    public record AggregatedMarketOffer(string StoreSlug, string StoreName, string SellerName, decimal Price, string Url, bool IsDirectProductUrl = false);

    private record SeedPhoneEntry(string Slug, string Name, decimal LowestPrice, IReadOnlyList<AggregatedMarketOffer> Offers);

    public static bool IsDirectProductUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var u = url.ToLowerInvariant();
        if (u.Contains("/sr?q=") ||
            u.Contains("/ara?q=") ||
            u.Contains("/arama") ||
            u.Contains("/s?k=") ||
            u.Contains("search.html") ||
            u.Contains("/faturaya-ek/") ||
            u.EndsWith("/cihazlar") ||
            u.EndsWith("/tr/iphone/") ||
            u.EndsWith("/tr/phone/") ||
            u.EndsWith("/mobil/cep-telefonu/"))
        {
            return false;
        }

        return true;
    }

    public static bool IsRefurbishedOrUsed(string? text, string? url = null)
    {
        var combined = $"{text} {url}".ToLowerInvariant();
        return combined.Contains("yenilenmiş") ||
               combined.Contains("yenilenmis") ||
               combined.Contains("ikinci el") ||
               combined.Contains("ikinci-el") ||
               combined.Contains("a kalite") ||
               combined.Contains("a-kalite") ||
               combined.Contains("b kalite") ||
               combined.Contains("b-kalite") ||
               combined.Contains("c kalite") ||
               combined.Contains("c-kalite") ||
               combined.Contains("teşhir") ||
               combined.Contains("teshir") ||
               combined.Contains("outlet");
    }

    private static readonly string[] ModelTierKeywords =
    {
        "redmi", "note", "lite", "mini", "fe", "se", "plus", "ultra", "max", "pro",
        "fold", "flip", "neo", "gt", "nova", "reno", "spark", "camon", "blade", "focus",
        "zenfone", "rog", "razr", "edge", "moto", "nord", "open", "xperia", "pixel", "pova", "narzo"
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

        var q = Regex.Replace(query.ToLowerInvariant(), @"\(.*?\)", " ");
        q = Regex.Replace(q, @"\b(64|128|256|512|1024)\s*gb\b|\b1\s*tb\b", " ");
        q = Regex.Replace(q, @"\s+", " ").Trim();

        var c = candidateTitle.ToLowerInvariant().Replace("ui", "ul");
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
        if (q.Contains("se") && (c.Contains("2020") || c.Contains("2022") || c.Contains("se 2") || c.Contains("se 3")))
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
        if (!AllowedStoreSlugs.Contains(storeSlug))
        {
            return Array.Empty<StoreRawProduct>();
        }

        var cleanQuery = query.Trim();
        var allOffers = await FetchMarketOffersForQueryAsync(cleanQuery, cancellationToken);

        var matchingStoreOffers = allOffers
            .Where(o => o.StoreSlug.Equals(storeSlug, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.IsDirectProductUrl)
            .ThenBy(o => o.Price)
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

        // Data yoksa yoktur: Mağazanın doğrulanmış gerçek teklifi yoksa sahte fallback fiyat/link üretilmez.
        return Array.Empty<StoreRawProduct>();
    }

    public static async Task<IReadOnlyList<AggregatedMarketOffer>> FetchMarketOffersForQueryAsync(
        string query,
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
                if (!offersByStore.ContainsKey(seedOffer.StoreSlug) ||
                    (seedOffer.IsDirectProductUrl && !offersByStore[seedOffer.StoreSlug].IsDirectProductUrl))
                {
                    offersByStore[seedOffer.StoreSlug] = seedOffer;
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
                    offersByStore["mediamarkt"] = mo;
                }
            }
        }
        catch { /* graceful fallback */ }

        // 3. Live Akakçe Multi-Store Scrape (with rate-limiting semaphore so bursts never get blocked)
        try
        {
            var akakceOffers = await ScrapeAkakceLiveAsync(query, seedEntry?.Slug, cancellationToken);
            foreach (var ao in akakceOffers)
            {
                if (!AllowedStoreSlugs.Contains(ao.StoreSlug))
                    continue;

                if (!offersByStore.TryGetValue(ao.StoreSlug, out var existing) ||
                    ao.IsDirectProductUrl ||
                    !existing.IsDirectProductUrl)
                {
                    // Preserve direct URL from seed/live if Akakce offer didn't resolve click redirect
                    var bestUrl = ao.IsDirectProductUrl
                        ? ao.Url
                        : (existing != null && existing.IsDirectProductUrl ? existing.Url : ao.Url);
                    var isDirect = IsDirectProductUrl(bestUrl);

                    offersByStore[ao.StoreSlug] = ao with
                    {
                        Url = bestUrl,
                        IsDirectProductUrl = isDirect
                    };
                }
            }
        }
        catch { /* graceful fallback */ }

        var results = offersByStore.Values
            .Where(o => AllowedStoreSlugs.Contains(o.StoreSlug) && o.Price >= 1500)
            .OrderBy(o => o.Price)
            .ThenByDescending(o => o.IsDirectProductUrl)
            .ToList();

        if (results.Any())
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
                            list.Add(new AggregatedMarketOffer("mediamarkt", "MediaMarkt", "MediaMarkt Resmi Mağaza", Math.Round(price, 2), prodUrl, true));
                            return list;
                        }
                    }
                }
            }
            catch { /* continue */ }
        }

        return list;
    }

    private static async Task<IReadOnlyList<AggregatedMarketOffer>> ScrapeAkakceLiveAsync(
        string query,
        string? knownSlug,
        CancellationToken cancellationToken)
    {
        var results = new List<AggregatedMarketOffer>();

        // Only wait briefly for Akakce rate lock; if busy (e.g. during parallel test runs), skip live Akakce and use seed/Pazarama/MediaMarkt
        if (!await AkakceRateLock.WaitAsync(TimeSpan.FromMilliseconds(1200), cancellationToken))
        {
            return results;
        }

        try
        {
            var elapsed = DateTime.UtcNow - _lastAkakceRequestUtc;
            if (elapsed < TimeSpan.FromMilliseconds(450))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(450) - elapsed, cancellationToken);
            }

            string? detailPath = null;
            if (!string.IsNullOrWhiteSpace(knownSlug) && KnownAkakceUrls.TryGetValue(knownSlug, out var mappedPath))
            {
                detailPath = mappedPath;
            }
            else
            {
                var cleanSearch = StripStorageSuffix(query);
                var searchUrl = $"https://www.akakce.com/arama/?q={Uri.EscapeDataString(cleanSearch)}";
                _lastAkakceRequestUtc = DateTime.UtcNow;
                using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                using var resp = await HttpClient.SendAsync(req, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    var searchHtml = await resp.Content.ReadAsStringAsync(cancellationToken);
                    var prodMatches = Regex.Matches(
                        searchHtml,
                        @"&quot;name&quot;:\[0,&quot;(?<name>[^&]+)&quot;\],&quot;hasLogo&quot;:\[0,[a-z]+\],&quot;imageUrl&quot;:\[0,&quot;[^&]*&quot;\],&quot;url&quot;:\[0,&quot;(?<url>/cep-telefonu/[^&]+?\.html)&quot;\]");

                    foreach (Match pm in prodMatches)
                    {
                        var candidateName = WebUtility.HtmlDecode(pm.Groups["name"].Value);
                        if (IsModelMatch(query, candidateName))
                        {
                            detailPath = pm.Groups["url"].Value;
                            break;
                        }
                    }
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

                    var seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int resolvedRedirects = 0;

                    foreach (Match mOffer in offerMatches)
                    {
                        var rawStore = WebUtility.HtmlDecode(mOffer.Groups["store"].Value).Trim();
                        var rawTitle = WebUtility.HtmlDecode(mOffer.Groups["name"].Value).Trim();
                        var priceStr = mOffer.Groups["price"].Value;

                        if (!decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price < 3000)
                            continue;

                        if (IsRefurbishedOrUsed(rawTitle))
                            continue;

                        var mappedSlug = MapStoreSlug(rawStore, "");
                        if (string.IsNullOrWhiteSpace(mappedSlug) || !seenSlugs.Add(mappedSlug))
                            continue;

                        string? directUrl = null;
                        if (resolvedRedirects < 2)
                        {
                            directUrl = await TryResolveAkakceDirectUrlAsync(
                                mOffer.Groups["s"].Value,
                                mOffer.Groups["v"].Value,
                                mOffer.Groups["p"].Value,
                                mOffer.Groups["c"].Value,
                                mOffer.Groups["g"].Value,
                                mOffer.Groups["f"].Value,
                                cancellationToken);

                            if (!string.IsNullOrWhiteSpace(directUrl))
                            {
                                resolvedRedirects++;
                            }
                        }

                        var finalUrl = !string.IsNullOrWhiteSpace(directUrl)
                            ? directUrl
                            : BaseStoreAdapter.ResolveSafeStoreUrl(mappedSlug, null, rawTitle);

                        var displayName = GetStoreDisplayName(mappedSlug);
                        results.Add(new AggregatedMarketOffer(
                            mappedSlug,
                            displayName,
                            $"{displayName} Doğrulanmış Satıcı",
                            Math.Round(price, 2),
                            finalUrl,
                            IsDirectProductUrl(finalUrl)));
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
                m = Regex.Match(html, @"url=(?<u1>https?://[^""']+)");
            }

            if (m.Success)
            {
                var cleanUrl = Regex.Replace(m.Groups["u1"].Value, @"[?&](utm_source|wt_pc|adj_t|tag|linkCode)=.*$", "");
                return cleanUrl;
            }
        }
        catch { /* ignore redirect failure */ }

        return null;
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

    private static decimal ApplyFallbackStoreMargin(decimal basePrice, string storeSlug, string query)
    {
        // Strictly > 1.025m so a synthetic fallback store offer NEVER undercuts a real verified store offer!
        var hash = Math.Abs((query.ToLowerInvariant() + ":" + storeSlug.ToLowerInvariant()).GetHashCode());
        var step = (hash % 14) + 5; // 5 to 18 -> +2.75% to +9.9% above basePrice
        var multiplier = 1.000m + (step * 0.0055m);
        return Math.Round(basePrice * multiplier, 0);
    }

    public static decimal EstimateBasePrice(string query)
    {
        var seed = FindSeedEntry(query);
        if (seed != null && seed.LowestPrice > 0)
        {
            return seed.LowestPrice;
        }

        var q = query.ToLowerInvariant();
        decimal price = 34999m;

        // Apple Models
        if (q.Contains("mate xt")) price = 140057m;
        else if (q.Contains("17 pro max")) price = 105699m;
        else if (q.Contains("iphone air")) price = 59014m;
        else if (q.Contains("16 pro max")) price = 93499m;
        else if (q.Contains("16 pro")) price = 84959m;
        else if (q.Contains("16 plus")) price = 80968m;
        else if (q.Contains("iphone 16")) price = 69399m;
        else if (q.Contains("15 pro max")) price = 74299m;
        else if (q.Contains("15 pro")) price = 73999m;
        else if (q.Contains("15 plus")) price = 58999m;
        else if (q.Contains("iphone 15")) price = 56449m;
        else if (q.Contains("14 pro max")) price = 64999m;
        else if (q.Contains("14 pro")) price = 56999m;
        else if (q.Contains("iphone 14")) price = 43999m;
        else if (q.Contains("iphone 13")) price = 35999m;
        else if (q.Contains("iphone se")) price = 27061m;

        // Samsung Models
        else if (q.Contains("s26 ultra")) price = 82971m;
        else if (q.Contains("s25 ultra")) price = 69997m;
        else if (q.Contains("s25+") || q.Contains("s25 plus")) price = 53000m;
        else if (q.Contains("s25")) price = 44052m;
        else if (q.Contains("s24 ultra")) price = 74512m;
        else if (q.Contains("s24 fe")) price = 29017m;
        else if (q.Contains("s23 fe")) price = 22025m;
        else if (q.Contains("z fold 6")) price = 79012m;
        else if (q.Contains("z flip 6")) price = 46008m;
        else if (q.Contains("a56")) price = 19018m;
        else if (q.Contains("a55")) price = 16500m;
        else if (q.Contains("a36")) price = 15033m;
        else if (q.Contains("a35")) price = 13312m;
        else if (q.Contains("a25")) price = 10559m;
        else if (q.Contains("a16")) price = 9019m;
        else if (q.Contains("a15")) price = 7552m;
        else if (q.Contains("m55")) price = 14055m;

        // Xiaomi / Redmi / POCO / Honor / Huawei / vivo / OPPO / OnePlus
        else if (q.Contains("mix fold 4")) price = 65028m;
        else if (q.Contains("15 pro")) price = 43001m;
        else if (q.Contains("xiaomi 15")) price = 54999m;
        else if (q.Contains("14t pro")) price = 35999m;
        else if (q.Contains("xiaomi 14")) price = 32024m;
        else if (q.Contains("13 ultra")) price = 39016m;
        else if (q.Contains("pura 70 ultra")) price = 57199m;
        else if (q.Contains("magic v3")) price = 75027m;
        else if (q.Contains("magic 7 pro")) price = 55029m;
        else if (q.Contains("magic 6 pro")) price = 59499m;
        else if (q.Contains("x200 pro")) price = 64499m;
        else if (q.Contains("x100 pro")) price = 44052m;
        else if (q.Contains("find x8 pro")) price = 49015m;
        else if (q.Contains("oneplus 13")) price = 50066m;
        else if (q.Contains("oneplus open")) price = 70064m;

        return Math.Round(price, 2);
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
                            // Include direct product URLs or top 3 cheapest seed offers
                            offers.Add(new AggregatedMarketOffer(stSlug, stName, seller, p, u, isDirect));
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
