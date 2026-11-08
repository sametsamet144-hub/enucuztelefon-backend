using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/products")]
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

    [HttpGet("{slug}/offers")]
    public async Task<IActionResult> GetOffersByPhoneSlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _productService.GetOffersByPhoneSlugAsync(slug, cancellationToken);
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
