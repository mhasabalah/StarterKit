using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Models;
using Identity.Models;
using Identity.Options;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shared.Core.Authorization;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity.Tests.Sessions;

/// <summary>Role + amr claims as issued by the real JwtTokenGenerator and read back by a JWT bearer handler.</summary>
public sealed class TokenClaimsTests
{
    private static readonly JwtOptions Jwt = new()
    {
        SecretKey = "unit-test-jwt-secret-key-0123456789abcdef", Issuer = "issuer", Audience = "audience",
        AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 7
    };

    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly ApplicationUser _user = Mocks.User();

    public TokenClaimsTests()
    {
        _users.Setup(m => m.GetClaimsAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(new List<Claim>());
    }

    private JwtTokenGenerator Generator() =>
        new(Microsoft.Extensions.Options.Options.Create(Jwt), _users.Object, _time);

    /// <summary>Validates like JwtBearer does by default (JsonWebTokenHandler, inbound claim mapping ON).</summary>
    private static async Task<ClaimsIdentity> ReadAsync(string token)
    {
        var result = await new JsonWebTokenHandler { MapInboundClaims = true }.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Jwt.Issuer,
            ValidAudience = Jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.SecretKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        });
        result.IsValid.Should().BeTrue(result.Exception?.Message);
        return result.ClaimsIdentity;
    }

    [Fact]
    public async Task Token_carries_roles_but_no_role_derived_permissions()
    {
        var token = await Generator().GenerateTokenAsync(
            _user, [RoleConstants.User, RoleConstants.Finance, RoleConstants.Support, RoleConstants.Support]);

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();

        claims.Should().NotContain(c => c.Type == Permissions.ClaimType,
            "permissions are derived per request (PermissionClaimsTransformation), never issued");
        claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value)
            .Should().BeEquivalentTo([RoleConstants.User, RoleConstants.Finance, RoleConstants.Support]);
    }

    [Fact]
    public async Task Stored_user_claims_are_still_emitted_once()
    {
        _users.Setup(m => m.GetClaimsAsync(_user))
            .ReturnsAsync(new List<Claim> { new(Permissions.ClaimType, Permissions.CatalogModerate) });

        var token = await Generator().GenerateTokenAsync(_user, [RoleConstants.User]);

        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value)
            .Should().Equal(Permissions.CatalogModerate);
    }

    [Fact]
    public async Task Validated_token_roles_expand_to_the_deduplicated_permissions_of_all_roles()
    {
        var token = await Generator().GenerateTokenAsync(
            _user, [RoleConstants.User, RoleConstants.Finance, RoleConstants.Support, RoleConstants.Support]);

        var principal = await new PermissionClaimsTransformation()
            .TransformAsync(new ClaimsPrincipal(await ReadAsync(token)));

        var permissions = principal.FindAll(Permissions.ClaimType).Select(c => c.Value).ToList();
        permissions.Should().OnlyHaveUniqueItems();
        permissions.Should().BeEquivalentTo(
        [
            Permissions.BillingRead, Permissions.BillingApprove, Permissions.BillingRefund, Permissions.BillingExport,
            Permissions.PlansManage, Permissions.PaymentSettingsManage, Permissions.AuditRead, Permissions.UsersRead,
            Permissions.TenantsRead,
            Permissions.TenantsImpersonate
        ]);
    }

    [Fact]
    public async Task Plain_user_token_has_no_permission_claims_and_no_amr()
    {
        var token = await Generator().GenerateTokenAsync(_user, [RoleConstants.User]);

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();
        claims.Should().NotContain(c => c.Type == Permissions.ClaimType);
        claims.Should().NotContain(c => c.Type == AuthenticationMethods.ClaimType);
    }

    [Fact]
    public async Task Mfa_session_token_has_amr_mfa_that_survives_inbound_claim_mapping()
    {
        var token = await Generator().GenerateTokenAsync(_user, [RoleConstants.Admin], multiFactor: true);

        var raw = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Where(c => c.Type == AuthenticationMethods.ClaimType).Select(c => c.Value);
        raw.Should().BeEquivalentTo([AuthenticationMethods.Otp, AuthenticationMethods.Mfa]);

        var identity = await ReadAsync(token);
        AuthenticationMethods.IsMultiFactor(new ClaimsPrincipal(identity)).Should().BeTrue();
        new ClaimsPrincipal(identity).IsInRole(RoleConstants.Admin).Should().BeTrue("roles drive the derived permissions");
    }

    [Fact]
    public async Task Non_mfa_token_is_not_multi_factor_after_validation()
    {
        var token = await Generator().GenerateTokenAsync(_user, [RoleConstants.Admin]);

        AuthenticationMethods.IsMultiFactor(new ClaimsPrincipal(await ReadAsync(token))).Should().BeFalse();
    }

    [Fact]
    public async Task Impersonation_token_carries_the_actor_and_its_short_expiry()
    {
        var actor = Guid.NewGuid();
        var expiresAt = _time.GetUtcNow().UtcDateTime.AddMinutes(15);

        var token = await Generator().GenerateTokenAsync(_user, [RoleConstants.User],
            impersonation: new ImpersonationGrant(actor, expiresAt));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Single(c => c.Type == Shared.Core.MultiTenancy.TenantClaimTypes.Actor).Value.Should().Be(actor.ToString());
        jwt.ValidTo.Should().BeCloseTo(expiresAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Regular_tokens_have_no_actor()
    {
        var token = await Generator().GenerateTokenAsync(_user, [RoleConstants.User]);
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Should().NotContain(c => c.Type == Shared.Core.MultiTenancy.TenantClaimTypes.Actor);
    }

    [Fact]
    public async Task Mfa_flag_is_persisted_on_the_session_and_kept_by_refresh_rotation()
    {
        var jwt = new Mock<IJwtTokenGenerator>();
        jwt.Setup(j => j.GenerateTokenAsync(It.IsAny<ApplicationUser>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<bool>()))
            .ReturnsAsync("access");
        jwt.Setup(j => j.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString("N"));
        var repo = new Mock<IRefreshTokenRepository>();
        RefreshToken? issued = null, rotated = null;
        repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => issued = t);
        repo.Setup(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, RefreshToken, string?, CancellationToken>((_, t, _, _) => rotated = t)
            .ReturnsAsync(true);
        _users.WithUsers((_user, [RoleConstants.Admin]));
        var service = new AuthSessionService(_users.Object, jwt.Object, repo.Object,
            Microsoft.Extensions.Options.Options.Create(Jwt), _time, NullLogger<AuthSessionService>.Instance);

        var session = await service.IssueAsync(_user, "1.2.3.4", multiFactor: true);
        repo.Setup(r => r.FindByHashAsync(TokenHasher.Hash(session.RefreshToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(issued);
        await service.RefreshAsync(session.RefreshToken, "1.2.3.4");

        session.MultiFactor.Should().BeTrue();
        issued!.MultiFactor.Should().BeTrue();
        rotated!.MultiFactor.Should().BeTrue();
        jwt.Verify(j => j.GenerateTokenAsync(_user, It.IsAny<IReadOnlyCollection<string>>(), true), Times.Exactly(2));
    }

    [Fact]
    public async Task Default_session_is_not_mfa()
    {
        var jwt = new Mock<IJwtTokenGenerator>();
        jwt.Setup(j => j.GenerateTokenAsync(It.IsAny<ApplicationUser>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<bool>()))
            .ReturnsAsync("access");
        jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh");
        var repo = new Mock<IRefreshTokenRepository>();
        RefreshToken? issued = null;
        repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => issued = t);
        _users.WithUsers((_user, [RoleConstants.Admin]));

        await new AuthSessionService(_users.Object, jwt.Object, repo.Object,
                Microsoft.Extensions.Options.Options.Create(Jwt), _time, NullLogger<AuthSessionService>.Instance)
            .IssueAsync(_user, "1.2.3.4");

        issued!.MultiFactor.Should().BeFalse();
        jwt.Verify(j => j.GenerateTokenAsync(_user, It.IsAny<IReadOnlyCollection<string>>(), false), Times.Once);
    }
}
