using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EnUcuzTelefon.Application.DTOs;
using EnUcuzTelefon.Application.Services;
using EnUcuzTelefon.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace EnUcuzTelefon.Api.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return new AuthResponseDto(false, null, null, null, null, "Bu e-posta adresi zaten kayıtlı.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return new AuthResponseDto(false, null, null, null, null, errors);
        }

        await _userManager.AddToRoleAsync(user, "User");
        var token = GenerateJwtToken(user, "User");

        return new AuthResponseDto(true, token, user.Email, user.FullName, "User", "Kayıt başarılı.");
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return new AuthResponseDto(false, null, null, null, null, "E-posta veya şifre hatalı.");
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
        if (!result.Succeeded)
        {
            return new AuthResponseDto(false, null, null, null, null, "E-posta veya şifre hatalı.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var primaryRole = roles.FirstOrDefault() ?? "User";
        var token = GenerateJwtToken(user, primaryRole);

        return new AuthResponseDto(true, token, user.Email, user.FullName, primaryRole, "Giriş başarılı.");
    }

    private string GenerateJwtToken(ApplicationUser user, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _configuration["Jwt:SecretKey"] ?? "EnUcuzTelefonSuperSecretKey2026!MustBe32CharsLongAtLeast"));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "EnUcuzTelefon",
            audience: _configuration["Jwt:Audience"] ?? "EnUcuzTelefonApp",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
