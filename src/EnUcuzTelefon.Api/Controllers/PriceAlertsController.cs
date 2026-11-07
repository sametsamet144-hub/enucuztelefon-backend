using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/price-alerts")]
public class PriceAlertsController : BaseApiController
{
    private readonly IPriceAlertService _alertService;

    public PriceAlertsController(IPriceAlertService alertService)
    {
        _alertService = alertService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAlert([FromBody] CreatePriceAlertRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _alertService.CreateAlertAsync(request, User?.Identity?.Name, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAlerts([FromQuery] string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { error = "E-posta adresi gereklidir." });
        }

        var result = await _alertService.GetAlertsByEmailAsync(email, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAlert(Guid id, CancellationToken cancellationToken)
    {
        var result = await _alertService.DeleteAlertAsync(id, cancellationToken);
        return HandleResult(result);
    }
}
