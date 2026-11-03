using System.IdentityModel.Tokens.Jwt;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Token;
using Identity.Services.TwoFactor;
using Microsoft.Extensions.Logging.Abstractions;

namespace Identity.Tests.TwoFactor;

public sealed class TwoFactorChallengeTests
{
    private static readonly JwtOptions Jwt = new()
    {
        SecretKey = "unit-test-jwt-secret-key-0123456789abcdef", Issuer = "issuer", Audience = "audience",
        AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 7
    };

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly ApplicationUser _user = Mocks.User();

    private TwoFactorChallengeService Service(JwtOptions? jwt = null) => new(
        Microsoft.Extensions.Options.Options.Create(jwt ?? Jwt),
        Mock.Of<Microsoft.Extensions.Options.IOptionsMonitor<TwoFactorOptions>>(m =>
            m.CurrentValue == new TwoFactorOptions { ChallengeTokenLifetimeMinutes = 5 }),
        _clock,
        NullLogger<TwoFactorChallengeService>.Instance);

    [Fact]
    public void A_fresh_challenge_validates_to_the_user_and_stamp()
    {
        var challenge = Service().Create(_user);

        var claims = Service().Validate(challenge.Token);

        claims.Should().NotBeNull();
        claims!.UserId.Should().Be(_user.Id);
        claims.SecurityStampHash.Should().Be(TokenHasher.HashSecurityStamp(_user.SecurityStamp));
        challenge.ExpiresAt.Should().Be(_clock.GetUtcNow().UtcDateTime.AddMinutes(5));
    }

    [Fact]
    public void It_expires_after_five_minutes()
    {
        var challenge = Service().Create(_user);

        _clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Service().Validate(challenge.Token).Should().NotBeNull();

        _clock.Advance(TimeSpan.FromSeconds(1));
        Service().Validate(challenge.Token).Should().BeNull();
    }

    [Fact]
    public void A_tampered_challenge_is_rejected()
    {
        var token = Service().Create(_user).Token;
        var parts = token.Split('.');
        var otherUser = Service().Create(Mocks.User()).Token.Split('.');

        // Payload of another user with this signature, and a flipped signature character.
        Service().Validate($"{parts[0]}.{otherUser[1]}.{parts[2]}").Should().BeNull();
        Service().Validate($"{parts[0]}.{parts[1]}.{(parts[2][0] == 'A' ? 'B' : 'A')}{parts[2][1..]}").Should().BeNull();
        Service().Validate("not-a-jwt").Should().BeNull();
        Service().Validate(null).Should().BeNull();
    }

    [Fact]
    public void A_challenge_signed_with_another_secret_is_rejected()
    {
        var foreign = Service(new JwtOptions
        {
            SecretKey = "another-secret-key-0123456789abcdef-xyz", Issuer = Jwt.Issuer, Audience = Jwt.Audience
        }).Create(_user);

        Service().Validate(foreign.Token).Should().BeNull();
    }

    [Fact]
    public async Task An_access_token_is_not_a_challenge_and_a_challenge_is_not_an_access_token()
    {
        var users = Mocks.UserManager();
        users.Setup(m => m.GetClaimsAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<System.Security.Claims.Claim>());
        var accessToken = await new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Jwt), users.Object, _clock)
            .GenerateTokenAsync(_user, ["Admin"]);

        Service().Validate(accessToken).Should().BeNull();

        var challenge = new JwtSecurityTokenHandler().ReadJwtToken(Service().Create(_user).Token);
        challenge.Audiences.Should().NotContain(Jwt.Audience, "JWT bearer validates the audience, so it rejects it");
        challenge.Claims.Should().NotContain(c => c.Type == "permission" || c.Type.EndsWith("/role"));
    }
}
