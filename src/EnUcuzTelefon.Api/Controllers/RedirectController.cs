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
        "www.trendyol.com",
        "trendyol.com",
        "www.hepsiburada.com",
        "hepsiburada.com",
        "www.amazon.com.tr",
        "amazon.com.tr",
        "www.teknosa.com",
        "teknosa.com",
        "www.vatanbilgisayar.com",
        "vatanbilgisayar.com",
        "www.mediamarkt.com.tr",
        "mediamarkt.com.tr",
        "www.n11.com",
        "n11.com",
        "www.pazarama.com",
        "pazarama.com",
        "www.pttavm.com",
        "pttavm.com",
        "pasaj.turkcell.com.tr",
        "www.vodafone.com.tr",
        "bireysel.turktelekom.com.tr",
        "www.itopya.com",
        "www.sinerji.com.tr",
        "www.incehesap.com",
        "www.ciceksepeti.com",
        "www.apple.com",
        "shop.samsung.com",
        "www.mi.com"
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
