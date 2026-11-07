using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/search")]
public class SearchController : BaseApiController
{
    private readonly ISearchService _searchService;

    public SearchController(ISearchService searchService)
    {
        _searchService = searchService;
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<object>());
        }

        var result = await _searchService.SearchAsync(q, limit, cancellationToken);
        return Ok(result);
    }
}
