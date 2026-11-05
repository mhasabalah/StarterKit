using System.Security.Claims;
using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Onboarding.Commands;
using Identity.Data.Repository.Onboarding;
using Identity.Domains.Auth.Features.CompleteProfile;
using Identity.Domains.Auth.Features.ConfirmEmail;
using Identity.Domains.Auth.Features.ExternalLogin;
using Identity.Domains.Auth.Features.ForgotPassword;
using Identity.Domains.Auth.Features.RefreshToken;
using Identity.Domains.Auth.Features.SendEmailConfirmation;
using Identity.Domains.Auth.Features.VerifyResetOtp;
using Identity.Domains.Onboarding.Models;
using Identity.Domains.Roles.Models;
using Identity.Services.Sms;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Resources;
using Identity.Services.EmailConfirmation;
using Identity.Services.Otp;
using Identity.Services.Phone;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Shared.Core.Exceptions;
using Shared.Core.Phone;
using Shared.Core.Web;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;
using AuthRegister = Identity.Domains.Auth.Features.Register;
using OnboardingRegister = Identity.Domains.Onboarding.Features.Register;

namespace Identity.Tests.Auth;

internal static class AuthTestKit
{
    public const string Phone = "+14155552671";

    public static IPhoneNumberNormalizer Normalizer() => new PhoneNumberNormalizer(new PhoneValidationService());

    public static Mock<RoleManager<ApplicationRole>> RoleManager(bool roleExists = true)
    {
        var m = new Mock<RoleManager<ApplicationRole>>(
            new Mock<IRoleStore<ApplicationRole>>().Object, null!, null!, null!, null!);
        m.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(roleExists);
        m.Setup(r => r.CreateAsync(It.IsAny<ApplicationRole>())).ReturnsAsync(IdentityResult.Success);
        return m;
    }

    public static AuthSession Session() =>
        new("access", "refresh", DateTime.UtcNow.AddHours(1), [RoleConstants.User]);
}

public sealed class AuthRegisterHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<RoleManager<ApplicationRole>> _roles = AuthTestKit.RoleManager();
    private readonly Mock<IAuthSessionService> _sessions = new();
    private readonly Mock<IEmailConfirmationSender> _confirm = new();
    private readonly Mock<IHttpContextService> _http = new();

    public AuthRegisterHandlerTests()
    {
        _users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.WithUserQuery();
        _http.Setup(h => h.GetIpAddress()).Returns("1.1.1.1");
        _sessions.Setup(s => s.IssueAsync(It.IsAny<ApplicationUser>(), "1.1.1.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestKit.Session());
    }

    private AuthRegister.RegisterHandler Handler() => new(_users.Object, _roles.Object, _sessions.Object,
        AuthTestKit.Normalizer(), _http.Object, _confirm.Object, new KeyLocalizer<IdentityResource>(),
        NullLogger<AuthRegister.RegisterHandler>.Instance);

    [Fact]
    public async Task Email_registration_creates_the_user_assigns_the_role_sends_confirmation_and_issues_a_session()
    {
        var result = await Handler().Handle(new RegisterCommand("a@b.com", "Passw0rd!", "Passw0rd!", "A", "B"), default);

        result.AccessToken.Should().Be("access");
        _users.Verify(m => m.CreateAsync(It.Is<ApplicationUser>(u => u.Email == "a@b.com" && u.UserName == "a@b.com"), "Passw0rd!"));
        _users.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleConstants.User), Times.Once);
        _confirm.Verify(c => c.TrySendAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_before_anything_is_created()
    {
        _users.Setup(m => m.FindByEmailAsync("a@b.com")).ReturnsAsync(Mocks.User());

        await Handler().Invoking(h => h.Handle(new RegisterCommand("a@b.com", "x", "x", null, null), default))
            .Should().ThrowAsync<BadRequestException>();
        _users.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        _sessions.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Phone_registration_normalizes_the_number_and_uses_a_placeholder_email()
    {
        await Handler().Handle(new RegisterCommand(AuthTestKit.Phone, "Passw0rd!", "Passw0rd!", null, null), default);

        _users.Verify(m => m.CreateAsync(
            It.Is<ApplicationUser>(u => u.PhoneNumber == AuthTestKit.Phone &&
                                        u.Email!.EndsWith(ApplicationUser.PhonePlaceholderEmailDomain)), It.IsAny<string>()));
    }

    [Fact]
    public async Task Duplicate_phone_is_rejected()
    {
        var existing = Mocks.User(AuthTestKit.Phone);
        _users.WithUserQuery(existing);

        await Handler().Invoking(h => h.Handle(new RegisterCommand(AuthTestKit.Phone, "x", "x", null, null), default))
            .Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task Identity_failures_and_malformed_identifiers_become_bad_requests()
    {
        _users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "weak" }));

        await Handler().Invoking(h => h.Handle(new RegisterCommand("a@b.com", "x", "x", null, null), default))
            .Should().ThrowAsync<BadRequestException>();
        await Handler().Invoking(h => h.Handle(new RegisterCommand("nonsense", "x", "x", null, null), default))
            .Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task A_missing_user_role_is_created_on_the_fly()
    {
        var roles = AuthTestKit.RoleManager(roleExists: false);
        var handler = new AuthRegister.RegisterHandler(_users.Object, roles.Object, _sessions.Object,
            AuthTestKit.Normalizer(), _http.Object, _confirm.Object, new KeyLocalizer<IdentityResource>(),
            NullLogger<AuthRegister.RegisterHandler>.Instance);

        await handler.Handle(new RegisterCommand("a@b.com", "Passw0rd!", "Passw0rd!", null, null), default);

        roles.Verify(r => r.CreateAsync(It.Is<ApplicationRole>(x => x.Name == RoleConstants.User)), Times.Once);
    }

    [Theory]
    [InlineData("", "Passw0rd!", "Passw0rd!", false)]
    [InlineData("a@b.com", "short", "short", false)]
    [InlineData("a@b.com", "password1!", "password1!", false)]
    [InlineData("a@b.com", "Passw0rd!", "Different1!", false)]
    [InlineData("a@b.com", "Passw0rd!", "Passw0rd!", true)]
    public void Validator_enforces_identifier_and_password_rules(string id, string pw, string confirm, bool valid) =>
        new AuthRegister.RegisterCommandValidator(new KeyLocalizer<IdentityResource>(), new PhoneValidationService())
            .Validate(new RegisterCommand(id, pw, confirm, null, null)).IsValid.Should().Be(valid);
}

public sealed class OnboardingRegisterHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<IOnboardingProcessRepository> _repo = new();

    public OnboardingRegisterHandlerTests()
    {
        _users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
    }

    private OnboardingRegister.RegisterHandler Handler() => new(_users.Object, AuthTestKit.RoleManager().Object,
        _repo.Object, AuthTestKit.Normalizer(), NullLogger<OnboardingRegister.RegisterHandler>.Instance);

    private static RegisterOnboardingCommand Command(string? email = null) =>
        new(AuthTestKit.Phone, "A", "B", "Passw0rd!", "Passw0rd!", email);

    [Fact]
    public async Task New_phone_creates_an_unverified_user_and_starts_onboarding()
    {
        _users.WithUserQuery();

        var result = await Handler().Handle(Command(" "), default);

        _users.Verify(m => m.CreateAsync(
            It.Is<ApplicationUser>(u => u.PhoneNumber == AuthTestKit.Phone && !u.IsPhoneNumberVerified && u.Email == null),
            "Passw0rd!"));
        _repo.Verify(r => r.AddAsync(It.IsAny<OnboardingProcess>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        result.Status.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_verified_phone_cannot_register_again()
    {
        var existing = Mocks.User(AuthTestKit.Phone);
        existing.IsPhoneNumberVerified = true;
        _users.WithUserQuery(existing);

        await Handler().Invoking(h => h.Handle(Command(), default)).Should().ThrowAsync<ConflictException>();
        _repo.Verify(r => r.AddAsync(It.IsAny<OnboardingProcess>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Pending_verification_is_a_conflict_but_an_abandoned_one_restarts_onboarding()
    {
        var existing = Mocks.User(AuthTestKit.Phone);
        _users.WithUserQuery(existing);
        _repo.Setup(r => r.GetActiveByUserIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OnboardingProcess.Create(existing.Id));

        await Handler().Invoking(h => h.Handle(Command(), default)).Should().ThrowAsync<ConflictException>();

        _repo.Setup(r => r.GetActiveByUserIdAsync(existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OnboardingProcess?)null);
        var result = await Handler().Handle(Command(), default);

        result.UserId.Should().Be(existing.Id);
        _users.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Completed_onboarding_and_identity_failures_are_rejected()
    {
        var existing = Mocks.User(AuthTestKit.Phone);
        _users.WithUserQuery(existing);
        _repo.Setup(r => r.GetByUserIdAndStatusAsync(existing.Id, OnboardingStatus.Complete, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OnboardingProcess.Create(existing.Id));
        await Handler().Invoking(h => h.Handle(Command(), default)).Should().ThrowAsync<ConflictException>();

        _users.WithUserQuery();
        _users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "weak" }));
        await Handler().Invoking(h => h.Handle(Command(), default)).Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public void Validator_requires_names_phone_and_strong_matching_password()
    {
        var v = new OnboardingRegister.RegisterOnboardingCommandValidator(new PhoneValidationService());

        v.Validate(Command()).IsValid.Should().BeTrue();
        v.Validate(Command() with { FirstName = "" }).IsValid.Should().BeFalse();
        v.Validate(Command() with { PhoneNumber = "123" }).IsValid.Should().BeFalse();
        v.Validate(Command() with { ConfirmPassword = "nope" }).IsValid.Should().BeFalse();
        v.Validate(Command("not-an-email")).IsValid.Should().BeFalse();
    }
}

public sealed class RefreshTokenHandlerTests
{
    [Fact]
    public async Task Refresh_returns_the_rotated_tokens_using_the_callers_ip()
    {
        var sessions = new Mock<IAuthSessionService>();
        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetIpAddress()).Returns("3.3.3.3");
        sessions.Setup(s => s.RefreshAsync("old", "3.3.3.3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestKit.Session());

        var result = await new RefreshTokenHandler(sessions.Object, http.Object)
            .Handle(new RefreshTokenCommand("old"), default);

        result.AccessToken.Should().Be("access");
        result.RefreshToken.Should().Be("refresh");
    }

    [Fact]
    public async Task A_rejected_refresh_propagates_as_unauthorized()
    {
        var sessions = new Mock<IAuthSessionService>();
        sessions.Setup(s => s.RefreshAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedException("no"));

        await new RefreshTokenHandler(sessions.Object, Mock.Of<IHttpContextService>())
            .Invoking(h => h.Handle(new RefreshTokenCommand("x"), default)).Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public void Validator_requires_a_token() =>
        new RefreshTokenCommandValidator().Validate(new RefreshTokenCommand("")).IsValid.Should().BeFalse();
}

public sealed class ExternalLoginSuccessPathTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<SignInManager<ApplicationUser>> _signIn;
    private readonly Mock<IAuthSessionService> _sessions = new();
    private readonly ApplicationUser _user = Mocks.User();
    private ExternalLoginInfo _info;

    public ExternalLoginSuccessPathTests()
    {
        _signIn = new Mock<SignInManager<ApplicationUser>>(_users.Object, Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.IdentityOptions()),
            NullLogger<SignInManager<ApplicationUser>>.Instance, Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<ApplicationUser>>());
        _info = Info();
        _signIn.Setup(s => s.GetExternalLoginInfoAsync(It.IsAny<string?>())).ReturnsAsync(() => _info);
        _users.Setup(m => m.FindByLoginAsync("Google", "key")).ReturnsAsync(_user);
        _users.Setup(m => m.AddLoginAsync(It.IsAny<ApplicationUser>(), It.IsAny<UserLoginInfo>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        _user.IsPhoneNumberVerified = true;
        _sessions.Setup(s => s.IssueAsync(_user, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthTestKit.Session());
    }

    private static ExternalLoginInfo Info(string? email = "g@x.com") => new(
        new ClaimsPrincipal(new ClaimsIdentity(email is null ? [] : [new Claim(ClaimTypes.Email, email)])),
        "Google", "key", "Google");

    private ExternalLoginCallbackHandler Handler() => new(_signIn.Object, _users.Object, _sessions.Object,
        Mock.Of<ITwoFactorChallengeService>(), Mock.Of<IHttpContextService>(), new NoSsoEnforcement(),
        NullLogger<ExternalLoginCallbackHandler>.Instance);

    private static readonly ExternalLoginCallbackCommand Callback = new(null);

    [Fact]
    public async Task Initiation_builds_an_escaped_challenge_url()
    {
        var result = await new ExternalLoginHandler(NullLogger<ExternalLoginHandler>.Instance)
            .Handle(new ExternalLoginCommand("Google", "/a b?x=1"), default);

        result.RedirectUrl.Should().Be("/api/identity/auth/challenge/Google?returnUrl=%2Fa%20b%3Fx%3D1");
    }

    [Fact]
    public async Task Without_2fa_a_known_user_gets_a_persisted_session_and_last_login_is_stamped()
    {
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "key", false, false)).ReturnsAsync(SignInResult.Success);

        var result = await Handler().Handle(Callback, default);

        result.AccessToken.Should().Be("access");
        result.RequiresTwoFactor.Should().BeFalse();
        _user.LastLoginAt.Should().NotBeNull();
        _users.Verify(m => m.UpdateAsync(_user), Times.Once);
    }

    [Fact]
    public async Task Unverified_phone_gets_no_tokens_and_a_disabled_account_is_refused()
    {
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "key", false, false)).ReturnsAsync(SignInResult.Success);
        _user.IsPhoneNumberVerified = false;

        var result = await Handler().Handle(Callback, default);
        result.RequiresPhoneVerification.Should().BeTrue();
        result.AccessToken.Should().BeNull();
        _sessions.Invocations.Should().BeEmpty();

        _user.IsActive = false;
        await Handler().Invoking(h => h.Handle(Callback, default)).Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Provider_errors_missing_info_and_lockout_are_bad_requests()
    {
        await Handler().Invoking(h => h.Handle(new ExternalLoginCallbackCommand("denied"), default))
            .Should().ThrowAsync<BadRequestException>();

        _info = null!;
        await Handler().Invoking(h => h.Handle(Callback, default)).Should().ThrowAsync<BadRequestException>();

        _info = Info();
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "key", false, false)).ReturnsAsync(SignInResult.LockedOut);
        await Handler().Invoking(h => h.Handle(Callback, default)).Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task First_time_login_creates_a_confirmed_user_with_the_user_role_and_no_tokens()
    {
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "key", false, false)).ReturnsAsync(SignInResult.Failed);

        var result = await Handler().Handle(Callback, default);

        result.IsNewUser.Should().BeTrue();
        result.AccessToken.Should().BeNull();
        _users.Verify(m => m.CreateAsync(It.Is<ApplicationUser>(u => u.Email == "g@x.com" && u.EmailConfirmed)), Times.Once);
        _users.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), RoleConstants.User), Times.Once);
    }

    [Fact]
    public async Task First_time_login_links_to_an_existing_account_with_the_same_email_and_requires_an_email_claim()
    {
        _signIn.Setup(s => s.ExternalLoginSignInAsync("Google", "key", false, false)).ReturnsAsync(SignInResult.Failed);
        var existing = Mocks.User();
        _users.Setup(m => m.FindByEmailAsync("g@x.com")).ReturnsAsync(existing);

        var result = await Handler().Handle(Callback, default);

        result.IsNewUser.Should().BeFalse();
        _users.Verify(m => m.AddLoginAsync(existing, It.IsAny<UserLoginInfo>()), Times.Once);
        _users.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>()), Times.Never);

        _info = Info(email: null);
        await Handler().Invoking(h => h.Handle(Callback, default)).Should().ThrowAsync<BadRequestException>();
    }
}

public sealed class PasswordResetAndEmailHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<INotificationSender> _notifications = new();
    private readonly Mock<INotificationBuilder> _builder = new();
    private readonly Mock<ISmsService> _sms = new();
    private readonly Mock<IOtpCodeService> _otp = new();
    private string? _recipient;

    public PasswordResetAndEmailHandlerTests()
    {
        _notifications.Setup(n => n.ToEmail(It.IsAny<string>(), It.IsAny<NotificationCulture>(), It.IsAny<Guid?>()))
            .Callback<string, NotificationCulture, Guid?>((e, _, _) => _recipient = e).Returns(_builder.Object);
        _builder.Setup(b => b.WithEmail(It.IsAny<EmailTemplate>(), It.IsAny<Dictionary<string, object>?>(), It.IsAny<EmailAttachment[]?>()))
            .Returns(_builder.Object);
        _users.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("reset-token");
        _users.WithUserQuery();
    }

    private ForgotPasswordHandler Forgot() => new(_users.Object, _notifications.Object, _sms.Object, _otp.Object,
        AuthTestKit.Normalizer(),
        Microsoft.Extensions.Options.Options.Create(new PasswordResetOptions { FrontendBaseUrl = "https://app.test" }),
        new NoSsoEnforcement(), NullLogger<ForgotPasswordHandler>.Instance);

    private static ApplicationUser Confirmed()
    {
        var u = Mocks.User();
        u.EmailConfirmed = true;
        return u;
    }

    [Fact]
    public async Task Confirmed_email_gets_a_reset_mail_on_the_email_channel_with_the_same_generic_answer()
    {
        var user = Confirmed();
        _users.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);

        var result = await Forgot().Handle(new ForgotPasswordCommand(user.Email!), default);

        result.Message.Should().Be(ForgotPasswordHandler.GenericMessage);
        _recipient.Should().Be(user.Email);
        _builder.Verify(b => b.SendAsync(NotificationChannel.Email, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_unconfirmed_oauth_and_inactive_accounts_get_nothing_but_the_same_answer()
    {
        var unconfirmed = Mocks.User();
        var oauth = Confirmed();
        oauth.OAuthProvider = "Google";
        var inactive = Confirmed();
        inactive.IsActive = false;

        foreach (var u in new[] { unconfirmed, oauth, inactive })
        {
            _users.Setup(m => m.FindByEmailAsync(u.Email!)).ReturnsAsync(u);
            (await Forgot().Handle(new ForgotPasswordCommand(u.Email!), default)).Message
                .Should().Be(ForgotPasswordHandler.GenericMessage);
        }

        (await Forgot().Handle(new ForgotPasswordCommand("nobody@x.com"), default)).Message
            .Should().Be(ForgotPasswordHandler.GenericMessage);
        _builder.Verify(b => b.SendAsync(It.IsAny<NotificationChannel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verified_phone_gets_an_sms_code_and_failures_never_leak()
    {
        var user = Mocks.User(AuthTestKit.Phone);
        user.IsPhoneNumberVerified = true;
        _users.WithUserQuery(user);
        _otp.Setup(o => o.IssueAsync(user.Id, OtpPurpose.PasswordReset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpIssueResult("123456", DateTime.UtcNow.AddMinutes(5)));

        await Forgot().Handle(new ForgotPasswordCommand(AuthTestKit.Phone), default);
        _sms.Verify(s => s.SendVerificationCodeAsync(AuthTestKit.Phone, "123456"), Times.Once);

        _otp.Setup(o => o.IssueAsync(user.Id, OtpPurpose.PasswordReset, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OtpRateLimitedException("slow", TimeSpan.FromMinutes(1)));
        (await Forgot().Handle(new ForgotPasswordCommand(AuthTestKit.Phone), default)).Message
            .Should().Be(ForgotPasswordHandler.GenericMessage);
    }

    [Fact]
    public async Task Unverified_phone_gets_no_code()
    {
        _users.WithUserQuery(Mocks.User(AuthTestKit.Phone));

        await Forgot().Handle(new ForgotPasswordCommand(AuthTestKit.Phone), default);

        _otp.Invocations.Should().BeEmpty();
    }

    private VerifyResetOtpHandler Verify() =>
        new(_users.Object, _otp.Object, AuthTestKit.Normalizer(), NullLogger<VerifyResetOtpHandler>.Instance);

    [Fact]
    public async Task A_correct_reset_code_returns_a_reset_token()
    {
        var user = Mocks.User(AuthTestKit.Phone);
        user.IsPhoneNumberVerified = true;
        _users.WithUserQuery(user);
        _otp.Setup(o => o.VerifyAsync(user.Id, OtpPurpose.PasswordReset, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerificationResult(OtpVerificationStatus.Success, 3));

        var result = await Verify().Handle(new VerifyResetOtpCommand(AuthTestKit.Phone, "123456"), default);

        result.UserId.Should().Be(user.Id);
        result.ResetToken.Should().Be("reset-token");
    }

    [Theory]
    [InlineData(OtpVerificationStatus.Mismatch, VerifyResetOtpHandler.InvalidOtpMessage)]
    [InlineData(OtpVerificationStatus.Expired, VerifyResetOtpHandler.InvalidOtpMessage)]
    [InlineData(OtpVerificationStatus.Locked, "Too many incorrect attempts. Please request a new reset code.")]
    public async Task A_bad_code_is_rejected_without_a_token(OtpVerificationStatus status, string message)
    {
        var user = Mocks.User(AuthTestKit.Phone);
        user.IsPhoneNumberVerified = true;
        _users.WithUserQuery(user);
        _otp.Setup(o => o.VerifyAsync(user.Id, OtpPurpose.PasswordReset, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OtpVerificationResult(status, 0));

        var act = () => Verify().Handle(new VerifyResetOtpCommand(AuthTestKit.Phone, "000000"), default);

        (await act.Should().ThrowAsync<BadRequestException>()).Which.Message.Should().Be(message);
        _users.Verify(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task Unknown_or_unverified_phones_look_exactly_like_a_wrong_code()
    {
        var unverified = Mocks.User(AuthTestKit.Phone);
        _users.WithUserQuery(unverified);

        var act = () => Verify().Handle(new VerifyResetOtpCommand(AuthTestKit.Phone, "123456"), default);
        (await act.Should().ThrowAsync<BadRequestException>()).Which.Message.Should().Be(VerifyResetOtpHandler.InvalidOtpMessage);

        _users.WithUserQuery();
        (await act.Should().ThrowAsync<BadRequestException>()).Which.Message.Should().Be(VerifyResetOtpHandler.InvalidOtpMessage);
        _otp.Invocations.Should().BeEmpty();
    }

    // ---- ConfirmEmail / SendEmailConfirmation -------------------------------------------------------------------

    [Fact]
    public async Task Confirm_email_succeeds_with_a_valid_token()
    {
        var user = Mocks.User();
        _users.WithUsers((user, []));
        _users.Setup(m => m.ConfirmEmailAsync(user, "tok")).ReturnsAsync(IdentityResult.Success);

        var result = await new ConfirmEmailHandler(_users.Object, NullLogger<ConfirmEmailHandler>.Instance)
            .Handle(new ConfirmEmailCommand(user.Id, "tok"), default);

        result.Message.Should().Be(ConfirmEmailHandler.ConfirmedMessage);
    }

    [Fact]
    public async Task Confirm_email_gives_one_error_for_unknown_inactive_and_bad_token()
    {
        var inactive = Mocks.User(active: false);
        var active = Mocks.User();
        _users.WithUsers((inactive, []), (active, []));
        _users.Setup(m => m.ConfirmEmailAsync(active, "bad")).ReturnsAsync(IdentityResult.Failed());
        var handler = new ConfirmEmailHandler(_users.Object, NullLogger<ConfirmEmailHandler>.Instance);

        foreach (var cmd in new[]
                 {
                     new ConfirmEmailCommand(Guid.NewGuid(), "t"), new ConfirmEmailCommand(inactive.Id, "t"),
                     new ConfirmEmailCommand(active.Id, "bad")
                 })
        {
            (await handler.Invoking(h => h.Handle(cmd, default)).Should().ThrowAsync<BadRequestException>())
                .Which.Message.Should().Be(ConfirmEmailHandler.InvalidTokenMessage);
        }
    }

    [Fact]
    public async Task Send_confirmation_covers_sent_already_confirmed_no_email_and_unknown()
    {
        var sender = new Mock<IEmailConfirmationSender>();
        var handler = new SendEmailConfirmationHandler(_users.Object, sender.Object);
        var pending = Mocks.User();
        var confirmed = Mocks.User();
        confirmed.EmailConfirmed = true;
        var noEmail = Mocks.User();
        noEmail.Email = null;
        _users.WithUsers((pending, []), (confirmed, []), (noEmail, []));

        (await handler.Handle(new SendEmailConfirmationCommand(pending.Id), default)).Message
            .Should().Be(SendEmailConfirmationHandler.SentMessage);
        sender.Verify(s => s.SendAsync(pending, It.IsAny<CancellationToken>()), Times.Once);

        (await handler.Handle(new SendEmailConfirmationCommand(confirmed.Id), default)).Message
            .Should().Be(SendEmailConfirmationHandler.AlreadyConfirmedMessage);
        await handler.Invoking(h => h.Handle(new SendEmailConfirmationCommand(noEmail.Id), default))
            .Should().ThrowAsync<BadRequestException>();
        await handler.Invoking(h => h.Handle(new SendEmailConfirmationCommand(Guid.NewGuid()), default))
            .Should().ThrowAsync<NotFoundException>();
        sender.Verify(s => s.SendAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public sealed class CompleteProfileHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<IHttpContextService> _http = new();
    private readonly Mock<IEmailConfirmationSender> _confirm = new();

    public CompleteProfileHandlerTests() => _users.WithUserQuery();

    private CompleteProfileHandler Handler() => new(_users.Object, _http.Object, AuthTestKit.Normalizer(),
        _confirm.Object, NullLogger<CompleteProfileHandler>.Instance);

    private ApplicationUser SignedIn(ApplicationUser user)
    {
        _http.Setup(h => h.GetUserIdAsGuid()).Returns(user.Id);
        _users.WithUsers((user, []));
        return user;
    }

    [Fact]
    public async Task Names_and_picture_are_updated_and_unset_fields_are_left_alone()
    {
        var user = SignedIn(Mocks.User());
        user.LastName = "Keep";

        await Handler().Handle(new CompleteProfileCommand("New", null, null, null, "https://img"), default);

        user.FirstName.Should().Be("New");
        user.LastName.Should().Be("Keep");
        user.ProfilePictureUrl.Should().Be("https://img");
        _users.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task A_phone_only_account_can_add_an_email_which_is_unconfirmed_and_triggers_confirmation()
    {
        var user = SignedIn(Mocks.User(AuthTestKit.Phone));
        user.Email = $"14155552671{ApplicationUser.PhonePlaceholderEmailDomain}";

        var result = await Handler().Handle(new CompleteProfileCommand(null, null, "me@x.com", null, null), default);

        user.Email.Should().Be("me@x.com");
        user.EmailConfirmed.Should().BeFalse();
        result.RequiresEmailVerification.Should().BeTrue();
        _confirm.Verify(c => c.TrySendAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_existing_real_email_is_never_replaced_and_a_taken_email_conflicts()
    {
        var user = SignedIn(Mocks.User());
        var original = user.Email;

        await Handler().Handle(new CompleteProfileCommand(null, null, "other@x.com", null, null), default);
        user.Email.Should().Be(original);
        _confirm.Invocations.Should().BeEmpty();

        var phoneOnly = SignedIn(Mocks.User(AuthTestKit.Phone));
        phoneOnly.Email = null;
        _users.Setup(m => m.FindByEmailAsync("taken@x.com")).ReturnsAsync(Mocks.User());
        await Handler().Invoking(h => h.Handle(new CompleteProfileCommand(null, null, "taken@x.com", null, null), default))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task An_email_account_can_add_a_phone_that_starts_unverified_and_a_taken_phone_conflicts()
    {
        var user = SignedIn(Mocks.User());

        var result = await Handler().Handle(new CompleteProfileCommand(null, null, null, AuthTestKit.Phone, null), default);

        user.PhoneNumber.Should().Be(AuthTestKit.Phone);
        user.IsPhoneNumberVerified.Should().BeFalse();
        result.RequiresPhoneVerification.Should().BeTrue();
        result.IsComplete.Should().BeFalse();

        var other = SignedIn(Mocks.User());
        _users.WithUserQuery(Mocks.User(AuthTestKit.Phone));
        await Handler().Invoking(h => h.Handle(new CompleteProfileCommand(null, null, null, AuthTestKit.Phone, null), default))
            .Should().ThrowAsync<ConflictException>();
        other.PhoneNumber.Should().BeNull();
    }

    [Fact]
    public async Task Identity_update_failures_surface_and_unauthenticated_or_missing_users_are_refused()
    {
        var user = SignedIn(Mocks.User());
        _users.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "boom" }));
        await Handler().Invoking(h => h.Handle(new CompleteProfileCommand("x", null, null, null, null), default))
            .Should().ThrowAsync<BadRequestException>();

        _http.Setup(h => h.GetUserIdAsGuid()).Returns((Guid?)null);
        await Handler().Invoking(h => h.Handle(new CompleteProfileCommand("x", null, null, null, null), default))
            .Should().ThrowAsync<ForbiddenException>();

        _http.Setup(h => h.GetUserIdAsGuid()).Returns(Guid.NewGuid());
        await Handler().Invoking(h => h.Handle(new CompleteProfileCommand("x", null, null, null, null), default))
            .Should().ThrowAsync<NotFoundException>();
    }
}
