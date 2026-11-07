using EnUcuzTelefon.Infrastructure.Adapters;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/redirect")]
public class RedirectController : ControllerBase
{
    private readonly ILogger<RedirectController> _logger;

    private static readonly HashSet<string> AllowedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.hepsiburada.com",
        "hepsiburada.com",
        "www.trendyol.com",
        "trendyol.com",
        "www.amazon.com.tr",
        "amazon.com.tr",
        "www.n11.com",
        "n11.com",
        "www.pttavm.com",
        "pttavm.com",
        "www.ciceksepeti.com",
        "ciceksepeti.com",
        "www.mediamarkt.com.tr",
        "mediamarkt.com.tr",
        "www.teknosa.com",
        "teknosa.com",
        "www.vatanbilgisayar.com",
        "vatanbilgisayar.com",
        "pasaj.turkcell.com.tr",
        "www.turkcell.com.tr",
        "turkcell.com.tr",
        "www.vodafone.com.tr",
        "vodafone.com.tr",
        "bireysel.turktelekom.com.tr",
        "www.turktelekom.com.tr",
        "turktelekom.com.tr",
        "www.apple.com",
        "apple.com",
        "shop.samsung.com",
        "www.samsung.com",
        "samsung.com",
        "www.mi.com",
        "mi.com",
        "consumer.huawei.com",
        "huawei.com",
        "www.honor.com",
        "honor.com",
        "www.tecno-mobile.com",
        "tecno-mobile.com",
        "tr.infinixmobility.com",
        "infinixmobility.com",
        "myepoint.com",
        "www.vivo.com",
        "vivo.com",
        "www.evkur.com.tr",
        "evkur.com.tr",
        "www.yonavm.com.tr",
        "yonavm.com.tr",
        "www.mutluevim.com.tr",
        "mutluevim.com.tr",
        "www.arcelik.com.tr",
        "arcelik.com.tr",
        "www.beko.com.tr",
        "beko.com.tr",
        "www.vestel.com.tr",
        "vestel.com.tr",
        "www.a101.com.tr",
        "a101.com.tr",
        "www.bim.com.tr",
        "bim.com.tr",
        "www.kurumsalsok.com.tr",
        "kurumsalsok.com.tr",
        "www.sokmarket.com.tr",
        "sokmarket.com.tr",
        "sok.com.tr"
    };

    public RedirectController(ILogger<RedirectController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult RedirectToStore(
        [FromQuery] string store = "trendyol",
        [FromQuery] string product = "cep telefonu",
        [FromQuery] string? url = null,
        [FromQuery] string? mode = null)
    {
        var safeUrl = BaseStoreAdapter.ResolveSafeStoreUrl(store, url, product);

        if (!Uri.TryCreate(safeUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !AllowedDomains.Contains(uri.Host))
        {
            safeUrl = BaseStoreAdapter.ResolveSafeStoreUrl(store, null, product);
        }

        _logger.LogInformation("Outbound store redirect: Store={Store}, Product={Product}, TargetUrl={TargetUrl}",
            store, product, safeUrl);

        if (string.Equals(mode, "json", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                store,
                product,
                targetUrl = safeUrl,
                verified = true,
                timestamp = DateTime.UtcNow
            });
        }

        return Redirect(safeUrl);
    }
}
