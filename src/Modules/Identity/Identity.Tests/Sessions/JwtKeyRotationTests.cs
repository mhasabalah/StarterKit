using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Identity.Tests.Sessions;

public sealed class JwtKeyRotationTests
{
    private const string OldKey = "old-signing-key-0123456789-abcdefghij";
    private const string NewKey = "new-signing-key-0123456789-abcdefghij";
    private const string Stranger = "stranger-signing-key-0123456789-abcdef";

    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    public JwtKeyRotationTests() =>
        _users.Setup(m => m.GetClaimsAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<Claim>());

    private static JwtOptions Options(string current, params string[] previous) => new()
    {
        SecretKey = current, PreviousSecretKeys = previous, Issuer = "i", Audience = "a"
    };

    private async Task<string> IssueAsync(JwtOptions options) =>
        await new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(options), _users.Object, _time)
            .GenerateTokenAsync(Mocks.User(), [RoleConstants.User]);

    /// <summary>Validates exactly like the JwtBearer setup in IdentityModule does.</summary>
    private static async Task<bool> ValidatesAsync(string token, JwtOptions options) =>
        (await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKeys = JwtSigningKeys.ValidationKeys(options),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true
        })).IsValid;

    [Fact]
    public async Task Token_header_carries_the_kid_of_the_current_key_and_never_the_key()
    {
        var token = await IssueAsync(Options(NewKey, OldKey));

        var jwt = new JsonWebToken(token);
        jwt.Kid.Should().Be(JwtSigningKeys.KeyId(NewKey)).And.HaveLength(16);
        jwt.Kid.Should().NotBe(JwtSigningKeys.KeyId(OldKey));
        token.Should().NotContain(NewKey);
    }

    [Fact]
    public async Task Only_the_current_key_configured_works_exactly_as_before()
    {
        var options = Options(NewKey);

        (await ValidatesAsync(await IssueAsync(options), options)).Should().BeTrue();
    }

    [Fact]
    public async Task Token_signed_with_a_previous_key_still_validates_after_rotation()
    {
        var issuedBeforeRotation = await IssueAsync(Options(OldKey));

        (await ValidatesAsync(issuedBeforeRotation, Options(NewKey, OldKey))).Should().BeTrue();
    }

    [Fact]
    public async Task Token_signed_with_a_previous_key_is_rejected_once_the_key_is_dropped()
    {
        var issuedBeforeRotation = await IssueAsync(Options(OldKey));

        (await ValidatesAsync(issuedBeforeRotation, Options(NewKey))).Should().BeFalse();
    }

    [Fact]
    public async Task Token_signed_with_an_unknown_key_is_rejected()
    {
        var forged = await IssueAsync(Options(Stranger));

        (await ValidatesAsync(forged, Options(NewKey, OldKey))).Should().BeFalse();
    }

    [Fact]
    public async Task Token_with_a_known_kid_but_a_forged_signature_is_rejected()
    {
        var forged = await IssueAsync(Options(Stranger));
        var parts = forged.Split('.');
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $$"""{"alg":"HS256","typ":"JWT","kid":"{{JwtSigningKeys.KeyId(NewKey)}}"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        (await ValidatesAsync($"{header}.{parts[1]}.{parts[2]}", Options(NewKey, OldKey))).Should().BeFalse();
    }

    [Fact]
    public void Previous_keys_are_deduplicated_and_never_repeat_the_current_one()
    {
        var keys = JwtSigningKeys.ValidationKeys(Options(NewKey, OldKey, OldKey, NewKey, ""));

        keys.Select(k => k.KeyId).Should().Equal(JwtSigningKeys.KeyId(NewKey), JwtSigningKeys.KeyId(OldKey));
    }

    [Fact]
    public void Previous_keys_must_be_strong_enough_too()
    {
        var result = new JwtOptionsValidator().Validate(null, Options(NewKey, OldKey, "short"));

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Should().Contain("PreviousSecretKeys:1");
        new JwtOptionsValidator().Validate(null, Options(NewKey, OldKey)).Succeeded.Should().BeTrue();
    }
}
