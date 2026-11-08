using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EnUcuzTelefon.Shared.Utils;

public static class StoreUrlHelper
{
    private static readonly Regex HepsiburadaSyntheticPattern = new(@"-p-HBCV(?!0000)[0-9A-Z]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CiceksepetiSyntheticPattern = new(@"-(kc|p)-?[0-9]{8,10}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TeknosaSyntheticPattern = new(@"-p-515[0-9]{6}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, Regex> ProductPathByHost = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hepsiburada.com"]       = new(@"-(p|pm)-HB[A-Z0-9]+/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["trendyol.com"]          = new(@"-p-\d{5,}/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["amazon.com.tr"]         = new(@"/(dp|gp/product)/[A-Z0-9]{10}", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["n11.com"]               = new(@"^/urun/.+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["mediamarkt.com.tr"]     = new(@"-\d{6,}\.html$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["teknosa.com"]           = new(@"-p-\d{6,}/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["vatanbilgisayar.com"]   = new(@"^/[^/]+\.html$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        ["pttavm.com"]            = new(@"-p-\d{6,}/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private static readonly (Regex Pattern, string Slug)[] MiModels =
    {
        (new(@"\bredmi\s*note\s*14\s*pro\s*(\+|plus)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "redmi-note-14-pro-plus-5g"),
        (new(@"\bredmi\s*note\s*14\s*pro\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled), "redmi-note-14-pro-5g"),
        (new(@"\bredmi\s*note\s*14\b",                 RegexOptions.IgnoreCase | RegexOptions.Compiled), "redmi-note-14"),
        (new(@"\bredmi\s*note\s*13\s*pro\s*(\+|plus)", RegexOptions.IgnoreCase | RegexOptions.Compiled), "redmi-note-13-pro-plus-5g"),
        (new(@"\bredmi\s*note\s*13\s*pro\b",           RegexOptions.IgnoreCase | RegexOptions.Compiled), "redmi-note-13-pro-5g"),
        (new(@"\bredmi\s*note\s*13\b",                 RegexOptions.IgnoreCase | RegexOptions.Compiled), "redmi-note-13"),
        (new(@"\bxiaomi\s*14t\s*pro\b",                RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-14t-pro"),
        (new(@"\bxiaomi\s*14t\b",                      RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-14t"),
        (new(@"\bxiaomi\s*14\s*ultra\b",               RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-14-ultra"),
        (new(@"\bxiaomi\s*14\b",                       RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-14"),
        (new(@"\bxiaomi\s*13\s*ultra\b",               RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-13-ultra"),
        (new(@"\bxiaomi\s*13\s*pro\b",                 RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-13-pro"),
        (new(@"\bxiaomi\s*13\b",                       RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-13"),
        (new(@"\bxiaomi\s*15\s*pro\b",                 RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-15-pro"),
        (new(@"\bxiaomi\s*15\b",                       RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-15"),
        (new(@"\bmix\s*fold\s*4\b",                    RegexOptions.IgnoreCase | RegexOptions.Compiled), "xiaomi-mix-fold-4"),
        (new(@"\bpoco\s*f6\s*pro\b",                   RegexOptions.IgnoreCase | RegexOptions.Compiled), "poco-f6-pro"),
        (new(@"\bpoco\s*f6\b",                         RegexOptions.IgnoreCase | RegexOptions.Compiled), "poco-f6"),
        (new(@"\bpoco\s*x6\s*pro\b",                   RegexOptions.IgnoreCase | RegexOptions.Compiled), "poco-x6-pro-5g"),
        (new(@"\bpoco\s*x6\b",                         RegexOptions.IgnoreCase | RegexOptions.Compiled), "poco-x6-5g"),
        (new(@"\bpoco\s*m6\s*pro\b",                   RegexOptions.IgnoreCase | RegexOptions.Compiled), "poco-m6-pro"),
        (new(@"\bpoco\s*f7\s*pro\b",                   RegexOptions.IgnoreCase | RegexOptions.Compiled), "poco-f7-pro"),
        (new(@"\bredmi\s*k80\s*pro\b",                 RegexOptions.IgnoreCase | RegexOptions.Compiled), "redmi-k80-pro"),
    };

    public static bool IsSyntheticUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return true;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return true;

        var u = url.ToLowerInvariant();

        // 1. Placeholder IDs
        if (u.Contains("-10000000") || u.Contains("hbc0000100000") || u.Contains("placeholder"))
        {
            return true;
        }

        // 2. Synthetic Hepsiburada SKU: HBCV followed by random hash not starting with 0000
        if (u.Contains("hepsiburada.com") && HepsiburadaSyntheticPattern.IsMatch(url))
        {
            return true;
        }

        // 3. Synthetic CicekSepeti SKU: -kc followed by 8-10 random digits
        if (u.Contains("ciceksepeti.com") && CiceksepetiSyntheticPattern.IsMatch(url))
        {
            return true;
        }

        // 4. Synthetic Teknosa fake SKU
        if (u.Contains("teknosa.com") && TeknosaSyntheticPattern.IsMatch(url))
        {
            return true;
        }

        return false;
    }

    public static bool IsDirectProductUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (IsSyntheticUrl(url)) return false;
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
            u.Contains("/mobil/cep-telefonu"))
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
        if (path.EndsWith("/buy-iphone") || path.EndsWith("/buy-iphone/"))
        {
            return false;
        }
        if (u.EndsWith("shop.samsung.com/tr") || u.EndsWith("shop.samsung.com/tr/"))
        {
            return false;
        }
        if (u.EndsWith("mi.com/tr/phone") || u.EndsWith("mi.com/tr/phone/"))
        {
            return false;
        }

        // Mi Store direct product pages
        if (u.Contains("mi.com/tr/product/"))
        {
            return true;
        }

        // Whitelist check for known store hosts
        var bareHost = host.StartsWith("www.") ? host[4..] : host;
        foreach (var (known, pattern) in ProductPathByHost)
        {
            if (bareHost.Equals(known, StringComparison.OrdinalIgnoreCase) || bareHost.EndsWith("." + known, StringComparison.OrdinalIgnoreCase))
                return pattern.IsMatch(uri.AbsolutePath);
        }

        return true;
    }

    public static string? TryResolveMiStoreSlug(string title)
    {
        foreach (var (pattern, slug) in MiModels)
            if (pattern.IsMatch(title ?? "")) return slug;
        return null;
    }

    public static string ResolveMiStoreUrl(string productTitle)
    {
        var slug = TryResolveMiStoreSlug(productTitle);
        return slug is null ? "https://www.mi.com/tr/phone/" : $"https://www.mi.com/tr/product/{slug}/";
    }

    public static string ResolveAppleStoreUrl(string productTitle)
    {
        var t = (productTitle ?? "").ToLowerInvariant();
        if (Regex.IsMatch(t, @"iphone\s*16\s*pro\s*max\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-16-pro";
        if (Regex.IsMatch(t, @"iphone\s*16\s*pro\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-16-pro";
        if (Regex.IsMatch(t, @"iphone\s*16\s*plus\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-16";
        if (Regex.IsMatch(t, @"iphone\s*16\b(?!\s*(e|plus|pro))")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-16";
        if (Regex.IsMatch(t, @"iphone\s*15\s*pro\s*max\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-15-pro";
        if (Regex.IsMatch(t, @"iphone\s*15\s*pro\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-15-pro";
        if (Regex.IsMatch(t, @"iphone\s*15\s*plus\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-15";
        if (Regex.IsMatch(t, @"iphone\s*15\b(?!\s*(plus|pro))")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-15";
        if (Regex.IsMatch(t, @"iphone\s*14\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-14";
        if (Regex.IsMatch(t, @"iphone\s*13\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-13";
        if (Regex.IsMatch(t, @"\bse\b")) return "https://www.apple.com/tr/shop/buy-iphone/iphone-se";
        return "https://www.apple.com/tr/iphone/";
    }

    public static string ResolveSamsungStoreUrl(string productTitle)
    {
        var t = (productTitle ?? "").ToLowerInvariant();
        if (Regex.IsMatch(t, @"\bs25\s*ultra\b")) return "https://www.samsung.com/tr/smartphones/galaxy-s25-ultra/buy/";
        if (Regex.IsMatch(t, @"\bs25\s*(\+|plus)\b")) return "https://www.samsung.com/tr/smartphones/galaxy-s25/buy/";
        if (Regex.IsMatch(t, @"\bs25\b(?!\s*(fe|edge|\+|plus|ultra))")) return "https://www.samsung.com/tr/smartphones/galaxy-s25/buy/";
        if (Regex.IsMatch(t, @"\bs24\s*ultra\b")) return "https://www.samsung.com/tr/smartphones/galaxy-s24-ultra/buy/";
        if (Regex.IsMatch(t, @"\bs24\s*fe\b")) return "https://www.samsung.com/tr/smartphones/galaxy-s24-fe/buy/";
        if (Regex.IsMatch(t, @"\bs24\s*(\+|plus)\b")) return "https://www.samsung.com/tr/smartphones/galaxy-s24/buy/";
        if (Regex.IsMatch(t, @"\bs24\b(?!\s*(fe|edge|\+|plus|ultra))")) return "https://www.samsung.com/tr/smartphones/galaxy-s24/buy/";
        if (Regex.IsMatch(t, @"fold\s*6\b")) return "https://www.samsung.com/tr/smartphones/galaxy-z-fold6/buy/";
        if (Regex.IsMatch(t, @"flip\s*6\b")) return "https://www.samsung.com/tr/smartphones/galaxy-z-flip6/buy/";
        return "https://shop.samsung.com/tr/mobil/cep-telefonu/";
    }

    public static string ResolveSafeStoreUrl(string storeSlug, string? rawUrl, string productTitle)
    {
        var slug = (storeSlug ?? "").ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(rawUrl) &&
            !IsSyntheticUrl(rawUrl) &&
            Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            var u = rawUrl.ToLowerInvariant();
            if (slug == "mistore" && (u.EndsWith("/phone") || u.EndsWith("/phone/")))
            {
                return ResolveMiStoreUrl(productTitle);
            }
            if (slug == "applestore" && (u.EndsWith("/iphone") || u.EndsWith("/iphone/")))
            {
                return ResolveAppleStoreUrl(productTitle);
            }
            if (slug == "samsungstore" && u.Contains("/mobil/cep-telefonu"))
            {
                return ResolveSamsungStoreUrl(productTitle);
            }
            return rawUrl;
        }

        var q = Uri.EscapeDataString(string.IsNullOrWhiteSpace(productTitle) ? "cep telefonu" : productTitle.Trim());
        return slug switch
        {
            "hepsiburada" => $"https://www.hepsiburada.com/ara?q={q}",
            "trendyol" => $"https://www.trendyol.com/sr?q={q}",
            "amazon" => $"https://www.amazon.com.tr/s?k={q}",
            "n11" => $"https://www.n11.com/arama?q={q}",
            "pttavm" => $"https://www.pttavm.com/arama?q={q}",
            "ciceksepeti" => $"https://www.ciceksepeti.com/arama?query={q}",
            "mediamarkt" => $"https://www.mediamarkt.com.tr/tr/search.html?query={q}",
            "teknosa" => $"https://www.teknosa.com/arama/?s={q}",
            "vatan" => $"https://www.vatanbilgisayar.com/arama/{q}/",
            "turkcell" => $"https://pasaj.turkcell.com.tr/arama?q={q}",
            "vodafone" => "https://www.vodafone.com.tr/faturaya-ek/telefonlar",
            "turk-telekom" => "https://bireysel.turktelekom.com.tr/cihazlar",
            "applestore" => ResolveAppleStoreUrl(productTitle),
            "samsungstore" => ResolveSamsungStoreUrl(productTitle),
            "mistore" => ResolveMiStoreUrl(productTitle),
            "huaweistore" => "https://consumer.huawei.com/tr/phones/",
            "honorstore" => "https://www.honor.com/tr/phones/",
            "tecnostore" => "https://www.tecno-mobile.com/tr/phones/",
            "infinixstore" => "https://tr.infinixmobility.com/smartphones",
            "vivostore" => "https://myepoint.com/akilli-telefonlar",
            "evkur" => $"https://www.evkur.com.tr/arama?q={q}",
            "yonavm" => $"https://www.yonavm.com.tr/arama?q={q}",
            _ => string.Empty
        };
    }
}
