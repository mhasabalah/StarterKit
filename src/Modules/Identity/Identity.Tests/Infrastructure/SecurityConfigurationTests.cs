using Identity.Infrastructure;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Identity.Tests.Infrastructure;

public sealed class SecurityConfigurationTests
{
    private static IHostEnvironment Env(string name) =>
        Mock.Of<IHostEnvironment>(e => e.EnvironmentName == name);

    [Theory]
    [InlineData("Production", "", false)]
    [InlineData("Production", "too-short", false)]
    [InlineData("Production", "0123456789abcdef0123456789abcdef", true)]
    [InlineData("Development", "", true)]
    [InlineData("Testing", "", true)]
    public void Otp_hmac_key_is_required_outside_development(string environment, string key, bool valid)
    {
        var result = new IdentitySecurityOptionsValidator(Env(environment))
            .Validate(null, new IdentitySecurityOptions { OtpHmacKey = key });

        result.Succeeded.Should().Be(valid);
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("0123456789abcdef0123456789abcde", false)] // 31 bytes
    [InlineData("0123456789abcdef0123456789abcdef", true)] // 32 bytes
    public void Jwt_key_must_be_at_least_32_bytes(string key, bool valid)
    {
        new JwtOptionsValidator()
            .Validate(null, new JwtOptions { SecretKey = key, Issuer = "i", Audience = "a" })
            .Succeeded.Should().Be(valid);
    }

    [Fact]
    public void Jwt_issuer_and_audience_are_required()
    {
        new JwtOptionsValidator()
            .Validate(null, new JwtOptions { SecretKey = new string('k', 32), Issuer = "", Audience = " " })
            .Failures.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("/api/auth/change-password", true)]
    [InlineData("/api/auth/logout", true)]
    [InlineData("/api/auth/refresh", true)]
    [InlineData("/api/auth/reset-password", true)]
    [InlineData("/api/v1/auth/logout", false)]
    [InlineData("/api/identity/users", false)]
    [InlineData("/api/auth/change-password-now", false)]
    public void Must_change_password_allow_list_matches_real_routes(string path, bool allowed)
    {
        MustChangePasswordMiddleware.IsAllowed(new PathString(path)).Should().Be(allowed);
    }

    [Fact]
    public async Task User_revocation_rejects_old_stamp_tokens_but_accepts_new_ones()
    {
        var cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
        var service = new TokenBlacklistService(cache, NullLogger<TokenBlacklistService>.Instance);
        var userId = Guid.NewGuid();

        (await service.IsUserTokenRevokedAsync(userId, "old")).Should().BeFalse("nothing revoked yet");

        await service.BlacklistUserAsync(userId, "new", TimeSpan.FromMinutes(5));

        (await service.IsUserTokenRevokedAsync(userId, "old")).Should().BeTrue();
        (await service.IsUserTokenRevokedAsync(userId, null)).Should().BeTrue();
        (await service.IsUserTokenRevokedAsync(userId, "new")).Should().BeFalse();
    }

    [Fact]
    public async Task Logged_out_access_tokens_are_blacklisted_by_hash()
    {
        var cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
        var service = new TokenBlacklistService(cache, NullLogger<TokenBlacklistService>.Instance);

        await service.BlacklistTokenAsync("jwt", DateTime.UtcNow.AddMinutes(5));

        (await service.IsTokenBlacklistedAsync("jwt")).Should().BeTrue();
        (await service.IsTokenBlacklistedAsync("other")).Should().BeFalse();
        (await cache.GetStringAsync("token:blacklist:jwt")).Should().BeNull("raw tokens are never stored");
    }
}
