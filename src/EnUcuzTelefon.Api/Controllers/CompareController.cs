using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/compare")]
public class CompareController : BaseApiController
{
    private readonly IProductService _productService;

    public CompareController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<IActionResult> CompareGet([FromQuery] string slugs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slugs))
        {
            return BadRequest(new { error = "En az bir telefon seçilmelidir." });
        }

        var slugList = slugs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return await Compare(new CompareRequestDto(slugList), cancellationToken);
    }

    [HttpPost]
    public async Task<IActionResult> Compare([FromBody] CompareRequestDto request, CancellationToken cancellationToken)
    {
        if (request?.PhoneSlugs == null || !request.PhoneSlugs.Any())
        {
            return BadRequest(new { error = "En az bir telefon seçilmelidir." });
        }

        var results = new List<PhoneDto>();
        foreach (var slug in request.PhoneSlugs.Distinct().Take(4))
        {
            var res = await _productService.GetPhoneBySlugAsync(slug, cancellationToken);
            if (res.IsSuccess)
            {
                results.Add(res.Value);
            }
        }

        return Ok(results);
    }
}
