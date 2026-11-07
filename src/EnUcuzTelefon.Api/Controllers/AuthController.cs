using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnUcuzTelefon.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        var result = await _authService.RegisterAsync(request);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var result = await _authService.LoginAsync(request);
        if (!result.Success)
        {
            return Unauthorized(result);
        }
        return Ok(result);
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        return Ok(new
        {
            email = User.Identity?.Name,
            isAuthenticated = User.Identity?.IsAuthenticated ?? false
        });
    }
}
