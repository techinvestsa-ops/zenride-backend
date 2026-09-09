using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Izigo.Infrastructure.Services;

public class TokenService(IConfiguration config) : ITokenService
{
    private readonly string _secret = config["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is required");
    private readonly string _issuer = config["Jwt:Issuer"] ?? "izigo-api";
    private readonly string _audience = config["Jwt:Audience"] ?? "izigo-apps";
    private readonly string _adminAudience = config["Jwt:AdminAudience"] ?? "izigo-admin";
    private readonly int _accessExpiry = int.Parse(config["Jwt:AccessExpiryMinutes"] ?? "60");
    private readonly int _adminAccessExpiry = int.Parse(config["Jwt:AdminAccessExpiryMinutes"] ?? "15");

    // App tokens (rider / driver)
    public string GenerateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Role, user.Role.ToString().ToLower()),
            new("role", user.Role.ToString().ToLower()),
            new("phone", user.Phone)
        };

        return CreateToken(claims, _audience, TimeSpan.FromMinutes(_accessExpiry));
    }

    public string GenerateRefreshToken() => GenerateSecureToken(64);

    public string? ValidateRefreshToken(string token) => token; // validation is via DB lookup

    // Admin tokens
    public string GenerateAdminAccessToken(Staff staff)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, staff.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("role", staff.RoleKey),
            new("email", staff.Email),
            new("markets", string.Join(",", staff.Markets)),
            new("is_staff", "true")
        };

        return CreateToken(claims, _adminAudience, TimeSpan.FromMinutes(_adminAccessExpiry));
    }

    public string GenerateAdminRefreshToken() => GenerateSecureToken(64);

    public string? ValidateAdminRefreshToken(string token) => token; // validation is via DB lookup

    private string CreateToken(IEnumerable<Claim> claims, string audience, TimeSpan expiry)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(expiry),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateSecureToken(int byteLength = 64)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes);
    }
}
