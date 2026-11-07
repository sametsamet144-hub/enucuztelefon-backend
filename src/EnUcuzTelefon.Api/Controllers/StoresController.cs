using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/stores")]
public class StoresController : BaseApiController
{
    private readonly IStoreService _storeService;

    public StoresController(IStoreService storeService)
    {
        _storeService = storeService;
    }

    [HttpGet]
    public async Task<IActionResult> GetStores(CancellationToken cancellationToken)
    {
        var result = await _storeService.GetStoresAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> GetStoreBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _storeService.GetStoreBySlugAsync(slug, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{slug}/status")]
    public async Task<IActionResult> GetStoreStatus(string slug, CancellationToken cancellationToken)
    {
        var result = await _storeService.GetStoreStatusAsync(slug, cancellationToken);
        return HandleResult(result);
    }
}
