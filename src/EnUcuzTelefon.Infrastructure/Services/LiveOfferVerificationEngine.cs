using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnUcuzTelefon.Application.Interfaces;
using EnUcuzTelefon.Infrastructure.Adapters;
using EnUcuzTelefon.Shared.Utils;
using Microsoft.Extensions.Logging;

namespace EnUcuzTelefon.Infrastructure.Services;

public class LiveOfferVerificationEngine : ILiveOfferVerificationEngine
{
    private const decimal AbsoluteMinPrice = 1000m;
    private const decimal AbsoluteMaxPrice = 350000m;
    private static readonly CultureInfo Tr = new("tr-TR");

    // Sadece <title>, <h1> ve og:title içinde aranır. Tüm HTML'de aramak JS bundle / i18n
    // metinlerindeki ifadeler yüzünden sağlıklı sayfaları "ölü" gösteriyordu.
    private static readonly string[] Soft404Phrases =
    {
        "ürün bulunamadı", "aradığınız ürün bulunamadı", "sayfa bulunamadı",
        "bu ürün artık satılmamaktadır", "aradığınız sayfa yayından kaldırılmış",
        "aradığınız sayfa mevcut değil", "sayfa mevcut değil"
    };

    private static readonly Regex TitleRegex = new(@"<title[^>]*>(?<t>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex H1Regex = new(@"<h1[^>]*>(?<t>.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TagStrip = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex JsonLdRegex = new(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(?<json>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex MetaTagRegex = new(@"<meta\s[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AttrRegex = new(@"(?<k>[\w:-]+)\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.Compiled);

    // Mağazaya özel son çare kalıpları: yalnızca İLGİLİ mağazaya uygulanır (eskiden tüm kalıplar her sayfada denenirdi).
    private static readonly Dictionary<string, Regex> StorePricePatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vatan"] = new(@"class=""product-list__price"">\s*(?<price>[\d.,]+)\s*</span>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["teknosa"] = new(@"class=""(?:prd-prc|p-price)"">\s*(?<price>[\d.,]+)\s*(?:TL|₺)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["trendyol"] = new(@"class=""prc-dsc"">\s*(?<price>[\d.,]+)\s*(?:TL|₺)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["mediamarkt"] = new(@"class=""price""[^>]*>\s*(?<price>[\d.,]+)\s*(?:TL|₺)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["n11"] = new(@"class=""newPrice""[^>]*>.*?<ins>\s*(?<price>[\d.,]+)\s*(?:TL|₺)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
        // Amazon: "a-price-whole" sayfada birden çok kez geçer (öneriler). İlk eşleşme yanlış ürün olabilir; JSON-LD yoksa dikkatli kullanın.
        ["amazon"] = new(@"class=""a-price-whole"">\s*(?<price>[\d.,]+)\s*</span>", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<LiveOfferVerificationEngine> _logger;

    public LiveOfferVerificationEngine(HttpClient httpClient, ILogger<LiveOfferVerificationEngine> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<LiveOfferVerificationResult> VerifyAndExtractOfferAsync(
        string url,
        string storeSlug,
        string phoneName,
        int? storageGb,
        decimal currentPrice,
        CancellationToken cancellationToken = default)
        => VerifyCoreAsync(url, storeSlug, phoneName, storageGb, currentPrice, allowRepair: true, cancellationToken);

    private async Task<LiveOfferVerificationResult> VerifyCoreAsync(
        string url,
        string storeSlug,
        string phoneName,
        int? storageGb,
        decimal currentPrice,
        bool allowRepair,
        CancellationToken ct)
    {
        // Onarım: yalnızca GERÇEK, doğrudan ve storage'a uyan bir ürün linki bulunursa ve o link
        // sayfadan doğrulanırsa kabul edilir. ASLA arama/kategori linkine "onarılmaz".
        async Task<LiveOfferVerificationResult> RepairOrDeadAsync(string reason)
        {
            if (!allowRepair) return Dead(reason);

            var cand = await TryFindRepairedOfferAsync(storeSlug, phoneName, storageGb, ct);
            if (cand != null && !string.Equals(cand.Url, url, StringComparison.OrdinalIgnoreCase))
            {
                var verified = await VerifyCoreAsync(cand.Url, storeSlug, phoneName, storageGb, currentPrice, allowRepair: false, ct);
                if (verified.Outcome == VerificationOutcome.Confirmed)
                {
                    _logger.LogInformation("Self-repaired {Phone} at {Store} ({Reason}): {Old} -> {New}",
                        phoneName, storeSlug, reason, url, cand.Url);
                    return verified with { DetectionSource = $"Repaired_{reason}", ResolvedUrl = cand.Url };
                }
            }

            return Dead(reason);
        }

        // 0. Boş / sahte / arama-kategori linki: eski veri. Onarılamıyorsa ölü sayılır (üç ardışık hatada kapanır).
        if (string.IsNullOrWhiteSpace(url) ||
            StoreUrlHelper.IsSyntheticUrl(url) ||
            !StoreUrlHelper.IsDirectProductUrl(url))
        {
            return await RepairOrDeadAsync("InvalidOrNonDirectUrl");
        }

        FetchResult fetch;
        try
        {
            fetch = await FetchAsync(url, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Network error verifying {Url}", url);
            return Inconclusive(url, "NetworkError");
        }

        // 1. Kesin ölü
        if (fetch.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return await RepairOrDeadAsync($"Http{(int)fetch.Status}");

        // 2. Bot koruması, 5xx, rate-limit vb.: BİLİNMİYOR. Ne ölü sayılır ne fiyat güncellenir ne "doğrulandı" denir.
        if ((int)fetch.Status < 200 || (int)fetch.Status >= 300)
            return Inconclusive(url, $"Http{(int)fetch.Status}");

        // 3. Yönlendirme ürün sayfasından çıkıp kategori/ana sayfaya düştüyse ürün artık yok
        if (!StoreUrlHelper.IsDirectProductUrl(fetch.FinalUrl))
            return await RepairOrDeadAsync("RedirectedAway");

        var html = fetch.Html;
        var meta = ParseMeta(html);
        var pageTitle = ExtractTagText(html, TitleRegex);

        // 4. Soft-404: yalnızca title / h1 / og:title
        if (IsSoft404(html, pageTitle, meta))
            return await RepairOrDeadAsync("Soft404");

        // 5. Sayfadan ürün bilgisi (JSON-LD Product > meta > mağazaya özel kalıp)
        var info = ExtractProductInfo(html, meta, pageTitle, storeSlug);

        // 6. Kimlik kontrolü: link artık başka model/depolama/yenilenmiş ürüne mi gidiyor?
        if (info.IsRefurbishedOrUsed ||
            MarketAggregator.IsRefurbishedOrUsed(info.Name, fetch.FinalUrl) ||
            IsRefurbishedPageHtml(html))
        {
            _logger.LogWarning("Refurbished/used offer rejected at {Url} ({Title})", url, info.Name);
            return await RepairOrDeadAsync("RefurbishedOrUsed");
        }

        if (!string.IsNullOrWhiteSpace(info.Name))
        {
            var modelOk = MarketAggregator.IsModelMatch(phoneName, info.Name, fetch.FinalUrl);
            var found = MarketAggregator.ExtractStorageGb(info.Name);
            var storageOk = !(storageGb is > 0) || !found.HasValue || found.Value == storageGb.Value;

            if (!modelOk || !storageOk)
            {
                _logger.LogWarning("Product mismatch at {Url}: expected '{Phone} {Storage}GB', page says '{Title}'",
                    url, phoneName, storageGb, info.Name);
                return await RepairOrDeadAsync("ProductMismatch");
            }
        }

        // 7. Fiyat sanity: mutlak aralık + mevcut fiyata göre aşırı sapma (aksesuar / taksit / başka varyant)
        decimal? price = null;
        if (info.Price is { } p && p >= AbsoluteMinPrice && p <= AbsoluteMaxPrice &&
            (currentPrice <= 0 || (p >= currentPrice * 0.35m && p <= currentPrice * 3m)))
        {
            price = Math.Round(p, 2);
        }

        var resolved = PickResolvedUrl(url, fetch.FinalUrl);
        return new LiveOfferVerificationResult(
            IsSuccess: true,
            IsUrlAlive: true,
            ExtractedPrice: price,
            ResolvedUrl: resolved,
            ExtractedTitle: info.Name,
            DetectionSource: info.Source,
            Outcome: VerificationOutcome.Confirmed,
            PriceFromProductPage: price.HasValue,
            InStock: info.InStock);
    }

    // ── sonuç yardımcıları ──────────────────────────────────────────────────
    private static LiveOfferVerificationResult Dead(string reason) => new(
        IsSuccess: true, IsUrlAlive: false, ExtractedPrice: null, ResolvedUrl: null,
        ExtractedTitle: null, DetectionSource: $"Dead_{reason}", Outcome: VerificationOutcome.Dead);

    private static LiveOfferVerificationResult Inconclusive(string url, string reason) => new(
        IsSuccess: true, IsUrlAlive: true, ExtractedPrice: null, ResolvedUrl: url,
        ExtractedTitle: null, DetectionSource: $"Inconclusive_{reason}", Outcome: VerificationOutcome.Inconclusive);

    private static string PickResolvedUrl(string original, string finalUrl)
    {
        // Ürün gerçekten taşındıysa (path değiştiyse) yeni adresi al; takip parametrelerini at.
        if (Uri.TryCreate(original, UriKind.Absolute, out var o) &&
            Uri.TryCreate(finalUrl, UriKind.Absolute, out var f) &&
            !string.Equals(o.AbsolutePath.TrimEnd('/'), f.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
            StoreUrlHelper.IsDirectProductUrl(finalUrl))
        {
            return f.GetLeftPart(UriPartial.Path);
        }

        return original;
    }

    // ── HTTP ────────────────────────────────────────────────────────────────
    private sealed record FetchResult(HttpStatusCode Status, string FinalUrl, string Html);

    private async Task<FetchResult> FetchAsync(string url, CancellationToken ct)
    {
        // Not: HttpClient'ın AllowAutoRedirect=true (varsayılan) olması gerekir; son URL buradan okunur.
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        req.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        req.Headers.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));

        using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var finalUrl = resp.RequestMessage?.RequestUri?.ToString() ?? url;
        var html = resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync(cts.Token) : string.Empty;
        return new FetchResult(resp.StatusCode, finalUrl, html);
    }

    // ── Soft 404 ────────────────────────────────────────────────────────────
    private static bool IsSoft404(string html, string? pageTitle, Dictionary<string, string> meta)
    {
        meta.TryGetValue("og:title", out var ogTitle);
        var h1 = ExtractTagText(html, H1Regex);
        var head = $"{pageTitle} {ogTitle} {h1}".ToLower(Tr);

        return Soft404Phrases.Any(p => head.Contains(p)) || Regex.IsMatch(head, @"\b404\b");
    }

    private static string? ExtractTagText(string html, Regex rx)
    {
        var m = rx.Match(html);
        if (!m.Success) return null;
        var text = WebUtility.HtmlDecode(TagStrip.Replace(m.Groups["t"].Value, " "));
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static bool IsRefurbishedPageHtml(string html)
    {
        return html.Contains("schema.org/RefurbishedCondition", StringComparison.OrdinalIgnoreCase)
            || html.Contains("schema.org/UsedCondition", StringComparison.OrdinalIgnoreCase)
            || html.Contains("schema.org/DamagedCondition", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Yenilenmiş: Mükemmel", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Yenilenmiş: Çok İyi", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Yenilenmiş: İyi", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Satış ve sevkiyat yenilegelsin", StringComparison.OrdinalIgnoreCase);
    }

    // ── Ürün bilgisi çıkarımı ───────────────────────────────────────────────
    private sealed record ProductInfo(string? Name, decimal? Price, bool? InStock, string Source, bool IsRefurbishedOrUsed = false);

    private static ProductInfo ExtractProductInfo(string html, Dictionary<string, string> meta, string? pageTitle, string storeSlug)
    {
        var ld = ExtractFromJsonLd(html);
        meta.TryGetValue("og:title", out var ogTitle);
        var name = ld?.Name ?? ogTitle ?? pageTitle;
        var isRefurbished = ld?.IsRefurbishedOrUsed ?? false;

        if (ld?.Price != null)
            return new ProductInfo(name, ld.Price, ld.InStock, "JsonLdProduct", isRefurbished);

        foreach (var key in new[] { "product:price:amount", "og:price:amount", "product:sale_price:amount", "price" })
        {
            if (meta.TryGetValue(key, out var raw) && ParseTurkishPrice(raw) is { } mp)
                return new ProductInfo(name, mp, ld?.InStock, "MetaTag", isRefurbished);
        }

        if (StorePricePatterns.TryGetValue(storeSlug, out var rx))
        {
            var m = rx.Match(html);
            if (m.Success && ParseTurkishPrice(m.Groups["price"].Value) is { } sp)
                return new ProductInfo(name, sp, ld?.InStock, "StoreMarkup", isRefurbished);
        }

        return new ProductInfo(name, null, ld?.InStock, "NoPrice", isRefurbished);
    }

    private sealed record JsonLdProduct(string? Name, decimal? Price, bool? InStock, bool IsRefurbishedOrUsed = false);

    private static JsonLdProduct? ExtractFromJsonLd(string html)
    {
        foreach (Match m in JsonLdRegex.Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups["json"].Value.Trim());
                foreach (var node in EnumerateNodes(doc.RootElement))
                {
                    if (!HasType(node, "Product") && !HasType(node, "ProductGroup")) continue;

                    var name = node.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    decimal? price = null;
                    bool? inStock = null;
                    bool isRefurbished = false;
                    if (node.TryGetProperty("offers", out var offers))
                        (price, inStock, isRefurbished) = ReadOffers(offers);

                    return new JsonLdProduct(name, price, inStock, isRefurbished);
                }
            }
            catch (JsonException) { /* bozuk JSON-LD bloğunu atla */ }
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateNodes(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in el.EnumerateArray())
                foreach (var y in EnumerateNodes(x))
                    yield return y;
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            yield return el;
            if (el.TryGetProperty("@graph", out var g))
                foreach (var y in EnumerateNodes(g))
                    yield return y;
            if (el.TryGetProperty("object", out var obj))
                foreach (var y in EnumerateNodes(obj))
                    yield return y;
        }
    }

    private static bool HasType(JsonElement node, string type)
    {
        if (!node.TryGetProperty("@type", out var t)) return false;
        return t.ValueKind switch
        {
            JsonValueKind.String => string.Equals(t.GetString(), type, StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String &&
                                                                string.Equals(x.GetString(), type, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static (decimal? Price, bool? InStock, bool IsRefurbishedOrUsed) ReadOffers(JsonElement offers)
    {
        var prices = new List<decimal>();
        var stocks = new List<bool>();
        var isRefurbished = false;

        void Collect(JsonElement o)
        {
            if (o.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in o.EnumerateArray()) Collect(x);
                return;
            }

            if (o.ValueKind != JsonValueKind.Object) return;

            if (o.TryGetProperty("itemCondition", out var cond) && cond.ValueKind == JsonValueKind.String)
            {
                var c = cond.GetString() ?? "";
                if (c.Contains("Refurbished", StringComparison.OrdinalIgnoreCase) ||
                    c.Contains("Used", StringComparison.OrdinalIgnoreCase) ||
                    c.Contains("Damaged", StringComparison.OrdinalIgnoreCase))
                {
                    isRefurbished = true;
                }
            }

            if (o.TryGetProperty("seller", out var seller) && seller.ValueKind == JsonValueKind.Object &&
                seller.TryGetProperty("name", out var sName) && sName.ValueKind == JsonValueKind.String &&
                MarketAggregator.IsRefurbishedOrUsed(sName.GetString()))
            {
                isRefurbished = true;
            }

            foreach (var key in new[] { "price", "lowPrice" })
            {
                if (o.TryGetProperty(key, out var p) && TryReadPrice(p, out var val))
                {
                    prices.Add(val);
                    break;
                }
            }

            if (o.TryGetProperty("availability", out var a) && a.ValueKind == JsonValueKind.String)
            {
                var s = a.GetString() ?? "";
                if (s.Contains("OutOfStock", StringComparison.OrdinalIgnoreCase) ||
                    s.Contains("SoldOut", StringComparison.OrdinalIgnoreCase) ||
                    s.Contains("Discontinued", StringComparison.OrdinalIgnoreCase))
                    stocks.Add(false);
                else if (s.Contains("InStock", StringComparison.OrdinalIgnoreCase) ||
                         s.Contains("LimitedAvailability", StringComparison.OrdinalIgnoreCase))
                    stocks.Add(true);
            }

            if (o.TryGetProperty("offers", out var nested)) Collect(nested);
        }

        Collect(offers);

        bool? inStock = stocks.Count == 0 ? null : stocks.Any(x => x);

        if (prices.Count == 0) return (null, inStock, isRefurbished);

        var min = prices.Min();
        var max = prices.Max();

        // Teklifler arasında %15'ten fazla fark varsa (farklı depolama/varyant karışmış olabilir) fiyat BELİRSİZ.
        if (max > min * 1.15m) return (null, inStock, isRefurbished);

        return (min, inStock, isRefurbished);
    }

    private static bool TryReadPrice(JsonElement p, out decimal value)
    {
        value = 0;
        if (p.ValueKind == JsonValueKind.Number) return p.TryGetDecimal(out value) && value > 0;
        if (p.ValueKind == JsonValueKind.String && ParseTurkishPrice(p.GetString()) is { } parsed)
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> ParseMeta(string html)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTagRegex.Matches(html))
        {
            string? key = null, content = null;
            foreach (Match a in AttrRegex.Matches(tag.Value))
            {
                var k = a.Groups["k"].Value.ToLowerInvariant();
                var v = WebUtility.HtmlDecode(a.Groups["v"].Value);
                if (k is "property" or "name" or "itemprop") key ??= v;
                else if (k == "content") content = v;
            }

            if (key != null && content != null) dict.TryAdd(key, content);
        }

        return dict;
    }

    // ── Fiyat ayrıştırma ────────────────────────────────────────────────────
    /// <summary>
    /// Düzeltilen hatalar: "7999.5" ve "69999.0" eskiden binlik ayraç sanılıp x10 / x1000 okunuyordu.
    /// Kural: tek ayraç + tam 3 hane sonrası => binlik; aksi halde ondalık. Birden çok aynı ayraç => binlik.
    /// </summary>
    public static decimal? ParseTurkishPrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var clean = raw.Replace("TL", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("TRY", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("₺", "");
        clean = Regex.Replace(clean, @"[^\d.,]", "");
        if (clean.Length == 0) return null;

        var lastDot = clean.LastIndexOf('.');
        var lastComma = clean.LastIndexOf(',');

        char? dec = null;
        if (lastDot >= 0 && lastComma >= 0)
            dec = lastComma > lastDot ? ',' : '.';
        else if (lastComma >= 0)
            dec = IsDecimalSeparator(clean, ',') ? ',' : null;
        else if (lastDot >= 0)
            dec = IsDecimalSeparator(clean, '.') ? '.' : null;

        string normalized;
        if (dec.HasValue)
        {
            var idx = clean.LastIndexOf(dec.Value);
            var intPart = new string(clean[..idx].Where(char.IsDigit).ToArray());
            var frac = new string(clean[(idx + 1)..].Where(char.IsDigit).ToArray());
            normalized = frac.Length == 0 ? intPart : $"{intPart}.{frac}";
        }
        else
        {
            normalized = new string(clean.Where(char.IsDigit).ToArray());
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var val) && val > 0
            ? Math.Round(val, 2)
            : null;
    }

    private static bool IsDecimalSeparator(string clean, char sep)
    {
        if (clean.Count(c => c == sep) != 1) return false;     // "1.299.990" => binlik
        var digitsAfter = clean.Length - clean.LastIndexOf(sep) - 1;
        return digitsAfter != 3;                                // "75.798" => binlik, "75.79" / "7999.5" => ondalık
    }

    // ── Onarım adayı (storage ve model duyarlı) ─────────────────────────────
    private static async Task<StoreRawProduct?> TryFindRepairedOfferAsync(
        string storeSlug, string phoneName, int? storageGb, CancellationToken ct)
    {
        try
        {
            var query = storageGb is > 0 ? $"{phoneName} {storageGb}GB" : phoneName;
            var offers = await MarketAggregator.GetOffersForStoreAsync(storeSlug, storeSlug, query, ct);

            return offers.FirstOrDefault(o =>
                o.Price >= 1500 &&
                StoreUrlHelper.IsDirectProductUrl(o.Url) &&
                MarketAggregator.IsModelMatch(phoneName, o.Title, o.Url) &&
                (!(storageGb is > 0) || o.StorageGb == storageGb));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
