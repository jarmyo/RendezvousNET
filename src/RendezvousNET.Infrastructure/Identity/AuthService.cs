using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using RendezvousNET.Application.Common;
using RendezvousNET.Application.Interfaces;
using RendezvousNET.Domain.Entities;
using RendezvousNET.Infrastructure.Persistence;
using RendezvousNET.Shared.DTOs.Auth;

namespace RendezvousNET.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IAppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    // In-memory refresh token store (replace with DB table in production)
    private static readonly Dictionary<string, (string UserId, DateTimeOffset ExpiresAt)> RefreshTokens = new();

    public AuthService(
        UserManager<AppUser> userManager,
        IAppDbContext db,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Result<AuthResponseDto>> RegisterAsync(
        RegisterRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var appUser = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName
        };

        var result = await _userManager.CreateAsync(appUser, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            return Result<AuthResponseDto>.Failure(errors);
        }

        // Create corresponding domain User record
        var domainUser = new User
        {
            Id = Guid.Parse(appUser.Id),
            Email = request.Email,
            DisplayName = request.DisplayName
        };
        _db.Add(domainUser);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<AuthResponseDto>.Success(await BuildAuthResponseAsync(appUser));
    }

    public async Task<Result<AuthResponseDto>> LoginAsync(
        LoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var appUser = await _userManager.FindByEmailAsync(request.Email);
        if (appUser is null || !await _userManager.CheckPasswordAsync(appUser, request.Password))
            return Result<AuthResponseDto>.Failure("Invalid email or password.");

        return Result<AuthResponseDto>.Success(await BuildAuthResponseAsync(appUser));
    }

    public Task<Result<AuthResponseDto>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (!RefreshTokens.TryGetValue(refreshToken, out var entry) ||
            entry.ExpiresAt < DateTimeOffset.UtcNow)
        {
            RefreshTokens.Remove(refreshToken);
            return Task.FromResult(Result<AuthResponseDto>.Failure("Invalid or expired refresh token."));
        }

        // Rotate token
        RefreshTokens.Remove(refreshToken);
        return _userManager.FindByIdAsync(entry.UserId).ContinueWith(async t =>
        {
            var user = t.Result;
            if (user is null)
                return Result<AuthResponseDto>.Failure("User not found.");
            return Result<AuthResponseDto>.Success(await BuildAuthResponseAsync(user));
        }, cancellationToken).Unwrap();
    }

    public Task<Result> RevokeRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        RefreshTokens.Remove(refreshToken);
        return Task.FromResult(Result.Success());
    }

    // --- Private helpers ---

    private Task<AuthResponseDto> BuildAuthResponseAsync(AppUser user)
    {
        var (accessToken, expiresAt) = GenerateJwt(user);
        var refreshToken = GenerateRefreshToken();

        RefreshTokens[refreshToken] = (user.Id, DateTimeOffset.UtcNow.AddDays(7));

        return Task.FromResult(new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User = new UserProfileDto
            {
                Id = Guid.Parse(user.Id),
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName
            }
        });
    }

    private (string token, DateTimeOffset expiresAt) GenerateJwt(AppUser user)
    {
        var jwtKey = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT key is not configured.");
        var issuer = _configuration["Jwt:Issuer"] ?? "RendezvousNET";
        var audience = _configuration["Jwt:Audience"] ?? "RendezvousNET";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim("displayName", user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}
