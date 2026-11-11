using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
[Route("api/v1/phones")]
public class ProductsController : BaseApiController
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] ProductFilterParams filterParams, CancellationToken cancellationToken)
    {
        var result = await _productService.GetPhonesAsync(filterParams, cancellationToken);
        return Ok(result);
    }

    [HttpGet("deals")]
    public async Task<IActionResult> GetDeals([FromQuery] int limit = 6, CancellationToken cancellationToken = default)
    {
        var result = await _productService.GetDealsAsync(limit, cancellationToken);
        return Ok(result);
    }

    [HttpGet("cheapest")]
    public async Task<IActionResult> GetCheapest([FromQuery] int limit = 6, CancellationToken cancellationToken = default)
    {
        var result = await _productService.GetCheapestAsync(limit, cancellationToken);
        return Ok(result);
    }

    [HttpGet("new-releases")]
    public async Task<IActionResult> GetNewReleases([FromQuery] int limit = 6, CancellationToken cancellationToken = default)
    {
        var result = await _productService.GetNewReleasesAsync(limit, cancellationToken);
        return Ok(result);
    }

    [HttpGet("popular")]
    public async Task<IActionResult> GetPopular([FromQuery] int limit = 6, CancellationToken cancellationToken = default)
    {
        var result = await _productService.GetPopularAsync(limit, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetProductBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _productService.GetPhoneBySlugAsync(slug, cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Belirtilen telefon ID'sine (GUID) göre tüm dükkanlardaki güncel fiyatları ve satın alma linklerini getirir.
    /// Sadece belirtilen ID'ye sahip telefonun mağaza tekliflerini ve doğrudan ürün bağlantılarını listeler.
    /// GET /api/v1/products/{id:guid}/prices
    /// </summary>
    /// <param name="id">Telefonun benzersiz kimlik numarası (GUID)</param>
    /// <param name="cancellationToken">İptal belirteci</param>
    /// <returns>Telefona ait tüm mağaza fiyatları ve linkleri</returns>
    [HttpGet("{id:guid}/prices")]
    [HttpGet("{id:guid}/store-prices")]
    [HttpGet("by-id/{id:guid}/prices")]
    [ProducesResponseType(typeof(PhonePricesResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPricesByPhoneId([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var result = await _productService.GetPricesByPhoneIdAsync(id, cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Query parametresi olarak verilen telefon ID'sine göre tüm dükkanlardaki fiyatları ve satın alma linklerini getirir.
    /// GET /api/v1/products/prices?phoneId=...
    /// </summary>
    /// <param name="phoneId">Telefonun benzersiz kimlik numarası (GUID)</param>
    /// <param name="cancellationToken">İptal belirteci</param>
    [HttpGet("prices")]
    [ProducesResponseType(typeof(PhonePricesResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPricesByPhoneIdQuery([FromQuery] Guid phoneId, CancellationToken cancellationToken)
    {
        if (phoneId == Guid.Empty)
        {
            return BadRequest(new { error = "Geçerli bir telefon ID'si (phoneId) belirtilmelidir.", code = "Validation.InvalidPhoneId" });
        }

        var result = await _productService.GetPricesByPhoneIdAsync(phoneId, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{slug}/offers")]
    public async Task<IActionResult> GetOffersByPhoneSlug(string slug, [FromQuery] int? storage, CancellationToken cancellationToken)
    {
        var result = await _productService.GetOffersByPhoneSlugAsync(slug, storage, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{idOrSlug}/prices")]
    public async Task<IActionResult> GetPrices(string idOrSlug, [FromQuery] int? storage, CancellationToken cancellationToken)
    {
        var result = await _productService.GetPricesByPhoneIdAsync(idOrSlug, storage, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{slug}/price-history")]
    public async Task<IActionResult> GetPriceHistory(string slug, CancellationToken cancellationToken)
    {
        var result = await _productService.GetPriceHistoryByPhoneSlugAsync(slug, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{slug}/alternatives")]
    public async Task<IActionResult> GetAlternatives(string slug, CancellationToken cancellationToken)
    {
        var current = await _productService.GetPhoneBySlugAsync(slug, cancellationToken);
        if (current.IsFailure) return HandleResult(current);

        // Fetch same brand or similar price range phones
        var alternatives = await _productService.GetPhonesAsync(
            new ProductFilterParams(Brand: current.Value.BrandSlug, PageSize: 5),
            cancellationToken);

        var filtered = alternatives.Items.Where(p => p.Id != current.Value.Id && p.Slug != current.Value.Slug).Take(4).ToList();
        return Ok(filtered);
    }
}
