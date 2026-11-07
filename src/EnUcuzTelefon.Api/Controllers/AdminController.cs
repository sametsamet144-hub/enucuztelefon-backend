using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
public class AdminController : BaseApiController
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var stats = await _adminService.GetDashboardStatsAsync(cancellationToken);
        return Ok(stats);
    }

    [HttpPost("stores/{slug}/sync")]
    public async Task<IActionResult> TriggerSync(string slug, CancellationToken cancellationToken)
    {
        var result = await _adminService.TriggerStoreSyncAsync(slug, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("sync-all")]
    public async Task<IActionResult> TriggerAllSync(CancellationToken cancellationToken)
    {
        var result = await _adminService.TriggerAllStoresSyncAsync(cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("anomalies")]
    public async Task<IActionResult> GetAnomalies(CancellationToken cancellationToken)
    {
        var anomalies = await _adminService.GetAnomaliesAsync(cancellationToken);
        return Ok(anomalies);
    }

    [HttpPost("anomalies/{id:guid}/resolve")]
    public async Task<IActionResult> ResolveAnomaly(Guid id, [FromQuery] bool approve, CancellationToken cancellationToken)
    {
        var result = await _adminService.ResolveAnomalyAsync(id, approve, User?.Identity?.Name, cancellationToken);
        return HandleResult(result);
    }
}
