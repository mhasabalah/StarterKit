using System.Security.Cryptography;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.Extensions.Options;

namespace Identity.Services.TwoFactor;

/// <summary>A signed, short-lived proof that the user passed the FIRST factor (password / external provider).</summary>
public sealed record TwoFactorChallenge(string Token, DateTime ExpiresAt);

/// <summary>
/// Issues and validates the two-factor login challenge token: an HS256 JWT that can NOT be used as an access token
/// (different signing key derived from the JWT secret, different audience, <c>token_use=2fa_challenge</c>, no roles or
/// permissions). It binds the user id and the security-stamp hash, so a password change / session revocation kills
/// outstanding challenges. Lifetime: <see cref="TwoFactorOptions.ChallengeTokenLifetimeMinutes"/> (default 5).
/// </summary>
public interface ITwoFactorChallengeService
{
    TwoFactorChallenge Create(ApplicationUser user);

    /// <summary>The user id when the token is authentic, unexpired and a challenge token; otherwise null.</summary>
    TwoFactorChallengeClaims? Validate(string? token);
}

public sealed record TwoFactorChallengeClaims(Guid UserId, string SecurityStampHash);

public sealed class TwoFactorChallengeService(
    IOptions<JwtOptions> jwtOptions,
    IOptionsMonitor<TwoFactorOptions> twoFactorOptions,
    TimeProvider timeProvider,
    ILogger<TwoFactorChallengeService> logger) : ITwoFactorChallengeService
{
    public const string TokenUseClaim = "token_use";
    public const string TokenUseValue = "2fa_challenge";
    public const string AudienceSuffix = ":2fa-challenge";

    private static readonly JwtSecurityTokenHandler Handler = new() { MapInboundClaims = false };

    private readonly JwtOptions _jwt = jwtOptions.Value;

    public TwoFactorChallenge Create(ApplicationUser user)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(Math.Clamp(twoFactorOptions.CurrentValue.ChallengeTokenLifetimeMinutes, 1, 15));
        var expires = now.Add(lifetime);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(TokenUseClaim, TokenUseValue),
                new Claim(IdentityClaimTypes.SecurityStampHash, TokenHasher.HashSecurityStamp(user.SecurityStamp))
            ],
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256));

        return new TwoFactorChallenge(Handler.WriteToken(token), expires);
    }

    public TwoFactorChallengeClaims? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var principal = Handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = SigningKey(),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                // Lifetime is checked below against the injected clock (testable, no clock skew).
                ValidateLifetime = false
            }, out var validated);

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (validated.ValidTo <= now || validated.ValidFrom > now.AddSeconds(5))
            {
                return null;
            }

            if (principal.FindFirst(TokenUseClaim)?.Value != TokenUseValue ||
                !Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId))
            {
                return null;
            }

            return new TwoFactorChallengeClaims(
                userId, principal.FindFirst(IdentityClaimTypes.SecurityStampHash)?.Value ?? string.Empty);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            logger.LogWarning("Rejected two-factor challenge token: {Reason}", ex.GetType().Name);
            return null;
        }
    }

    private string Audience => _jwt.Audience + AudienceSuffix;

    /// <summary>
    /// HMAC-SHA256(JWT secret, purpose): a key distinct from the access-token key, so an access token never validates
    /// as a challenge and vice versa.
    /// </summary>
    private SymmetricSecurityKey SigningKey() => new(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(_jwt.SecretKey), Encoding.UTF8.GetBytes("starterkit:2fa-challenge:v1")));
}
