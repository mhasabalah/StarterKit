using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Features.Login;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Phone;
using Identity.Domains.Auth.Services;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.Exceptions;
using Shared.Core.Web;

namespace Identity.Tests.Auth;

public sealed class LoginHandlerTests
{
    private readonly Mock<IPasswordHasher<ApplicationUser>> _hasher = new();
    private readonly Mock<UserManager<ApplicationUser>> _users;
    private readonly Mock<SignInManager<ApplicationUser>> _signIn;
    private readonly Mock<IAuthSessionService> _sessions = new();

    public LoginHandlerTests()
    {
        _hasher.Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>())).Returns("dummy-hash");
        _hasher.Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PasswordVerificationResult.Failed);
        _users = Mocks.UserManager(_hasher.Object);
        _signIn = new Mock<SignInManager<ApplicationUser>>(
            _users.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<ApplicationUser>>());
    }

    private readonly Mock<ITwoFactorChallengeService> _challenges = new();

    private LoginHandler Handler() => new(
        _users.Object,
        _signIn.Object,
        new LoginCompletionService(_users.Object, _sessions.Object, _challenges.Object, Mock.Of<IHttpContextService>(),
            Microsoft.Extensions.Options.Options.Create(new LoginPolicyOptions()), TimeProvider.System,
            NullLogger<LoginCompletionService>.Instance),
        Mock.Of<IPhoneNumberNormalizer>(),
        new NoSsoEnforcement(),
        NullLogger<LoginHandler>.Instance);

    [Fact]
    public async Task Unknown_user_and_wrong_password_produce_the_same_error()
    {
        var known = Mocks.User();
        _users.Setup(m => m.FindByEmailAsync(known.Email!)).ReturnsAsync(known);
        _signIn.Setup(s => s.CheckPasswordSignInAsync(known, It.IsAny<string>(), true)).ReturnsAsync(SignInResult.Failed);

        var unknown = await Handler().Invoking(h => h.Handle(new LoginCommand("nobody@example.com", "pw"), default))
            .Should().ThrowAsync<UnauthorizedException>();
        var wrongPassword = await Handler().Invoking(h => h.Handle(new LoginCommand(known.Email!, "pw"), default))
            .Should().ThrowAsync<UnauthorizedException>();

        unknown.Which.Message.Should().Be(LoginHandler.InvalidCredentialsMessage)
            .And.Be(wrongPassword.Which.Message);
        unknown.Which.StatusCode.Should().Be(wrongPassword.Which.StatusCode);
    }

    [Fact]
    public async Task Unknown_user_still_runs_a_password_hash_verification_for_timing_parity()
    {
        await Handler().Invoking(h => h.Handle(new LoginCommand("nobody@example.com", "secret"), default))
            .Should().ThrowAsync<UnauthorizedException>();

        _hasher.Verify(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>(), "secret"), Times.Once);
        _sessions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Deactivated_user_with_correct_password_gets_no_session()
    {
        var user = Mocks.User(active: false);
        _users.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        _signIn.Setup(s => s.CheckPasswordSignInAsync(user, "pw", true)).ReturnsAsync(SignInResult.Success);

        await Handler().Invoking(h => h.Handle(new LoginCommand(user.Email!, "pw"), default))
            .Should().ThrowAsync<UnauthorizedException>();

        _sessions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Successful_login_returns_the_issued_session()
    {
        var user = Mocks.User();
        _users.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        _signIn.Setup(s => s.CheckPasswordSignInAsync(user, "pw", true)).ReturnsAsync(SignInResult.Success);
        _sessions.Setup(s => s.IssueAsync(user, It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSession("access", "refresh", DateTime.UtcNow, ["User"]));

        var result = await Handler().Handle(new LoginCommand(user.Email!, "pw"), default);

        result.AccessToken.Should().Be("access");
        result.RefreshToken.Should().Be("refresh");
        result.User.Roles.Should().Equal("User");
        _users.Verify(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()), Times.Never, "roles come from the session");
    }

    [Fact]
    public async Task Correct_password_of_a_2fa_user_returns_a_challenge_and_no_session()
    {
        var user = Mocks.User();
        _users.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        _users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        _users.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(["Admin"]);
        _signIn.Setup(s => s.CheckPasswordSignInAsync(user, "pw", true)).ReturnsAsync(SignInResult.Success);
        var expires = DateTime.UtcNow.AddMinutes(5);
        _challenges.Setup(c => c.Create(user)).Returns(new TwoFactorChallenge("challenge-token", expires));

        var result = await Handler().Handle(new LoginCommand(user.Email!, "pw"), default);

        result.RequiresTwoFactor.Should().BeTrue();
        result.TwoFactorChallengeToken.Should().Be("challenge-token");
        result.TwoFactorChallengeExpiresAt.Should().Be(expires);
        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
        _sessions.Invocations.Should().BeEmpty("the password alone never yields a session for a 2FA account");
    }

    [Fact]
    public async Task Wrong_password_of_a_2fa_user_never_reveals_that_2fa_is_enabled()
    {
        var user = Mocks.User();
        _users.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        _users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(true);
        _signIn.Setup(s => s.CheckPasswordSignInAsync(user, "pw", true)).ReturnsAsync(SignInResult.Failed);

        await Handler().Invoking(h => h.Handle(new LoginCommand(user.Email!, "pw"), default))
            .Should().ThrowAsync<UnauthorizedException>().WithMessage(LoginHandler.InvalidCredentialsMessage);
        _challenges.Invocations.Should().BeEmpty();
    }
}
