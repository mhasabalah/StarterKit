using Identity.Options;
using Microsoft.Extensions.Options;

namespace Identity.Services.Token;

public interface IJwtTokenGenerator
{
    Task<string> GenerateTokenAsync(ApplicationUser user);
    string GenerateRefreshToken();
}

public class JwtTokenGenerator(
    IOptions<JwtOptions> jwtOptions,
    UserManager<ApplicationUser> userManager) : IJwtTokenGenerator
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<string> GenerateTokenAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var userClaims = await userManager.GetClaimsAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? ""),
            new("phone_number", user.PhoneNumber ?? ""),
            new("phone_verified", user.IsPhoneNumberVerified.ToString()),
            new("email_verified", user.EmailConfirmed.ToString()),
            new("profile_complete", user.IsProfileComplete.ToString()),
            new("full_name", user.FullName),
            new("must_change_password", user.MustChangePasswordOnNextLogin.ToString().ToLower())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        claims.AddRange(userClaims);

        if (roles.Contains("Admin"))
        {
            claims.Add(new Claim("is_admin", "true"));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }
}
