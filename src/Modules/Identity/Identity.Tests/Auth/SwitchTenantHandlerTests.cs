using System.Security.Claims;
using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Features.SwitchTenant;
using Identity.Services.Sessions;
using Shared.Core.Authorization;
using Shared.Core.Web;

namespace Identity.Tests.Auth;

/// <summary>A tenant switch must keep the MFA level of the current session, whatever claim type "amr" arrived as.</summary>
public sealed class SwitchTenantHandlerTests
{
    public static TheoryData<string?, bool> AmrClaims => new()
    {
        { AuthenticationMethods.MappedClaimType, true }, // JwtBearer default (MapInboundClaims = true)
        { AuthenticationMethods.ClaimType, true },       // raw claim (MapInboundClaims = false / other schemes)
        { null, false }                                  // single-factor session
    };

    [Theory]
    [MemberData(nameof(AmrClaims))]
    public async Task Carries_the_mfa_level_of_the_current_session(string? amrClaimType, bool expectedMultiFactor)
    {
        var user = Mocks.User();
        var users = Mocks.UserManager();
        users.WithUsers((user, ["User"]));
        var tenantId = Guid.NewGuid();

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()) };
        if (amrClaimType is not null)
        {
            claims.Add(new Claim(amrClaimType, AuthenticationMethods.Otp));
            claims.Add(new Claim(amrClaimType, AuthenticationMethods.Mfa));
        }

        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserId()).Returns(user.Id.ToString());
        http.Setup(h => h.GetAllClaims()).Returns(claims);

        var sessions = new Mock<IAuthSessionService>();
        sessions.Setup(s => s.SwitchTenantAsync(user, tenantId, It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSession("access", "refresh", DateTime.UtcNow.AddMinutes(15), ["User"], expectedMultiFactor, tenantId));

        var result = await new SwitchTenantHandler(users.Object, sessions.Object, http.Object)
            .Handle(new SwitchTenantCommand(tenantId), CancellationToken.None);

        result.TenantId.Should().Be(tenantId);
        sessions.Verify(s => s.SwitchTenantAsync(user, tenantId, It.IsAny<string?>(), expectedMultiFactor, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
