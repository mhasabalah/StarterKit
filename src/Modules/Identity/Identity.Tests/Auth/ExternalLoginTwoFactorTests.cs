using System.Security.Claims;
using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Features.ExternalLogin;
using Identity.Domains.Users.Models;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Web;

namespace Identity.Tests.Auth;

/// <summary>An external provider is only the first factor: it must never bypass the account's 2FA.</summary>
public sealed class ExternalLoginTwoFactorTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<SignInManager<ApplicationUser>> _signIn;
    private readonly Mock<IAuthSessionService> _sessions = new();
    private readonly Mock<ITwoFactorChallengeService> _challenges = new();
    private readonly ApplicationUser _user = Mocks.User();
    private readonly ExternalLoginInfo _info = new(new ClaimsPrincipal(new ClaimsIdentity()), "Google", "google-key", "Google");

    public ExternalLoginTwoFactorTests()
    {
        _signIn = new Mock<SignInManager<ApplicationUser>>(
            _users.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<ApplicationUser>>());
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(It.IsAny<string?>())).ReturnsAsync(_info);
        _users.Setup(m => m.FindByLoginAsync("Google", "google-key")).ReturnsAsync(_user);
        _user.IsPhoneNumberVerified = true;
        _challenges.Setup(c => c.Create(_user)).Returns(new TwoFactorChallenge("challenge", DateTime.UtcNow.AddMinutes(5)));
    }

    private ExternalLoginCallbackHandler Handler() => new(
        _signIn.Object, _users.Object, _sessions.Object, _challenges.Object, Mock.Of<IHttpContextService>(),
        new NoSsoEnforcement(), NullLogger<ExternalLoginCallbackHandler>.Instance);

    [Fact]
    public async Task External_login_asks_identity_not_to_bypass_two_factor()
    {
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "google-key", false, false))
            .ReturnsAsync(SignInResult.TwoFactorRequired);

        var result = await Handler().Handle(new ExternalLoginCallbackCommand(null), default);

        _signIn.Verify(s => s.ExternalLoginSignInAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), true),
            Times.Never);
        result.RequiresTwoFactor.Should().BeTrue();
        result.TwoFactorChallengeToken.Should().Be("challenge");
        result.AccessToken.Should().BeNull();
        _sessions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Even_a_reported_success_routes_a_2fa_user_into_the_challenge()
    {
        // e.g. a "remember this browser" 2FA cookie makes SignInManager report Succeeded.
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "google-key", false, false))
            .ReturnsAsync(SignInResult.Success);
        _users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(true);

        var result = await Handler().Handle(new ExternalLoginCallbackCommand(null), default);

        result.RequiresTwoFactor.Should().BeTrue();
        _sessions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task User_without_2fa_still_gets_a_session()
    {
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "google-key", false, false))
            .ReturnsAsync(SignInResult.Success);
        _sessions.Setup(s => s.IssueAsync(_user, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSession("access", "refresh", DateTime.UtcNow, ["User"]));

        var result = await Handler().Handle(new ExternalLoginCallbackCommand(null), default);

        result.RequiresTwoFactor.Should().BeFalse();
        result.AccessToken.Should().Be("access");
    }
}
