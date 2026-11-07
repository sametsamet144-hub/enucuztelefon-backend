using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/brands")]
public class BrandsController : BaseApiController
{
    private readonly IBrandService _brandService;
    private readonly IProductService _productService;

    public BrandsController(IBrandService brandService, IProductService productService)
    {
        _brandService = brandService;
        _productService = productService;
    }

    [HttpGet]
    public async Task<IActionResult> GetBrands(CancellationToken cancellationToken)
    {
        var result = await _brandService.GetBrandsAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetBrandBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _brandService.GetBrandBySlugAsync(slug, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{slug}/products")]
    public async Task<IActionResult> GetProductsByBrand(string slug, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var result = await _productService.GetPhonesAsync(new ProductFilterParams(Brand: slug, Page: page, PageSize: pageSize), cancellationToken);
        return Ok(result);
    }
}
