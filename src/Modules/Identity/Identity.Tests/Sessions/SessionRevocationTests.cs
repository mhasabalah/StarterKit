using Identity.Contracts.Auth.Commands;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Auth.Features.ChangePassword;
using Identity.Domains.Auth.Features.Logout;
using Identity.Domains.Auth.Features.ResetPassword;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.Web;

namespace Identity.Tests.Sessions;

public sealed class SessionRevocationTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<IRefreshTokenRepository> _repo = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<ISessionRevocationRecorder> _revocations = new();
    private readonly Mock<IHttpContextService> _http = new();
    private readonly Mock<ISessionRevoker> _revoker = new();
    private readonly ApplicationUser _user = Mocks.User();

    public SessionRevocationTests()
    {
        _users.WithUsers((_user, ["User"]));
        _http.Setup(h => h.GetUserIdAsGuid()).Returns(_user.Id);
    }

    [Fact]
    public async Task RevokeAll_rotates_stamp_revokes_refresh_tokens_and_blacklists_the_old_stamp()
    {
        var oldStamp = _user.SecurityStamp;
        _users.Setup(m => m.UpdateSecurityStampAsync(_user))
            .Callback(() => _user.SecurityStamp = "new-stamp")
            .ReturnsAsync(IdentityResult.Success);

        var revoker = new SessionRevoker(_users.Object, _repo.Object, _blacklist.Object, _revocations.Object, _http.Object,
            Microsoft.Extensions.Options.Options.Create(new JwtOptions { SecretKey = "x", Issuer = "i", Audience = "a", AccessTokenExpirationMinutes = 30 }),
            NullLogger<SessionRevoker>.Instance);

        await revoker.RevokeAllAsync(_user.Id, "test");

        _users.Verify(m => m.UpdateSecurityStampAsync(_user), Times.Once);
        _repo.Verify(r => r.RevokeAllForUserAsync(_user.Id, "test", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _blacklist.Verify(b => b.BlacklistUserAsync(_user.Id, TokenHasher.HashSecurityStamp("new-stamp"), TimeSpan.FromMinutes(31)), Times.Once);
        _revocations.Verify(r => r.RecordAllSessionsAsync(_user.Id, It.IsAny<CancellationToken>()), Times.Once);
        TokenHasher.HashSecurityStamp("new-stamp").Should().NotBe(TokenHasher.HashSecurityStamp(oldStamp));
    }

    [Fact]
    public async Task RevokeAll_for_an_unknown_user_records_no_announcement()
    {
        _users.Setup(m => m.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        var revoker = new SessionRevoker(_users.Object, _repo.Object, _blacklist.Object, _revocations.Object, _http.Object,
            Microsoft.Extensions.Options.Options.Create(new JwtOptions { SecretKey = "x", Issuer = "i", Audience = "a" }),
            NullLogger<SessionRevoker>.Instance);

        await revoker.RevokeAllAsync(Guid.NewGuid(), "test");

        _revocations.Verify(r => r.RecordAllSessionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAll_survives_a_redis_failure_after_the_database_revocation()
    {
        _users.Setup(m => m.UpdateSecurityStampAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _blacklist.Setup(b => b.BlacklistUserAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .ThrowsAsync(new TimeoutException("redis down"));

        var revoker = new SessionRevoker(_users.Object, _repo.Object, _blacklist.Object, _revocations.Object, _http.Object,
            Microsoft.Extensions.Options.Options.Create(new JwtOptions { SecretKey = "x", Issuer = "i", Audience = "a" }),
            NullLogger<SessionRevoker>.Instance);

        await revoker.Invoking(r => r.RevokeAllAsync(_user.Id, "test")).Should().NotThrowAsync();
        _repo.Verify(r => r.RevokeAllForUserAsync(_user.Id, "test", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_revokes_all_sessions_and_clears_must_change_password()
    {
        _user.MustChangePasswordOnNextLogin = true;
        _users.Setup(m => m.ResetPasswordAsync(_user, "tok", "N3w!Passw0rd")).ReturnsAsync(IdentityResult.Success);

        await new ResetPasswordHandler(_users.Object, _revoker.Object, NullLogger<ResetPasswordHandler>.Instance)
            .Handle(new ResetPasswordCommand(_user.Id, null, "tok", "N3w!Passw0rd", "N3w!Passw0rd"), default);

        _user.MustChangePasswordOnNextLogin.Should().BeFalse();
        _revoker.Verify(r => r.RevokeAllAsync(_user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failed_reset_does_not_revoke()
    {
        _users.Setup(m => m.ResetPasswordAsync(_user, It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken" }));

        var act = () => new ResetPasswordHandler(_users.Object, _revoker.Object, NullLogger<ResetPasswordHandler>.Instance)
            .Handle(new ResetPasswordCommand(_user.Id, null, "bad", "N3w!Passw0rd", "N3w!Passw0rd"), default);

        await act.Should().ThrowAsync<Shared.Core.Exceptions.BadRequestException>();
        _revoker.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_revokes_other_sessions_then_issues_a_new_one()
    {
        var calls = new List<string>();
        _users.Setup(m => m.ChangePasswordAsync(_user, "Old!Passw0rd", "N3w!Passw0rd")).ReturnsAsync(IdentityResult.Success);
        _revoker.Setup(r => r.RevokeAllAsync(_user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("revoke")).Returns(Task.CompletedTask);
        var sessions = new Mock<IAuthSessionService>();
        sessions.Setup(s => s.IssueAsync(_user, It.IsAny<string?>(), false, It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("issue"))
            .ReturnsAsync(new AuthSession("a", "r", DateTime.UtcNow, ["User"]));

        var result = await new ChangePasswordHandler(_users.Object, _revoker.Object, sessions.Object, _http.Object,
                NullLogger<ChangePasswordHandler>.Instance)
            .Handle(new ChangePasswordCommand(_user.Id, "Old!Passw0rd", "N3w!Passw0rd", "N3w!Passw0rd"), default);

        calls.Should().Equal("revoke", "issue");
        result.AccessToken.Should().Be("a");
        result.RefreshToken.Should().Be("r");
    }

    [Fact]
    public async Task ChangePassword_from_an_mfa_session_keeps_the_new_session_mfa()
    {
        _users.Setup(m => m.ChangePasswordAsync(_user, "Old!Passw0rd", "N3w!Passw0rd")).ReturnsAsync(IdentityResult.Success);
        _http.Setup(h => h.GetAllClaims()).Returns(
            [new System.Security.Claims.Claim(Shared.Core.Authorization.AuthenticationMethods.MappedClaimType, "mfa")]);
        var sessions = new Mock<IAuthSessionService>();
        sessions.Setup(s => s.IssueAsync(_user, It.IsAny<string?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthSession("a", "r", DateTime.UtcNow, ["Admin"], true));

        await new ChangePasswordHandler(_users.Object, _revoker.Object, sessions.Object, _http.Object,
                NullLogger<ChangePasswordHandler>.Instance)
            .Handle(new ChangePasswordCommand(_user.Id, "Old!Passw0rd", "N3w!Passw0rd", "N3w!Passw0rd"), default);

        sessions.Verify(s => s.IssueAsync(_user, It.IsAny<string?>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    private readonly Mock<ISessionManager> _sessions = new();

    private LogoutHandler Logout() => new(_blacklist.Object, _repo.Object, _revoker.Object, _sessions.Object, _http.Object,
        NullLogger<LogoutHandler>.Instance);

    [Fact]
    public async Task Logout_all_devices_revokes_every_session()
    {
        await Logout().Handle(new LogoutCommand(LogoutAllDevices: true), default);

        _revoker.Verify(r => r.RevokeAllAsync(_user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(s => s.RevokeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "all sessions are already gone");
    }

    [Fact]
    public async Task Single_logout_ends_the_callers_session_by_sid_not_just_the_presented_tokens()
    {
        var sid = Guid.NewGuid();
        _sessions.Setup(s => s.GetCurrentSessionId()).Returns(sid);

        await Logout().Handle(new LogoutCommand("raw-refresh"), default);

        // Family revoked + sid blacklisted (SessionManager): every access token of this session dies, not only this one.
        _sessions.Verify(s => s.RevokeAsync(_user.Id, sid, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.RevokeAsync(_user.Id, TokenHasher.Hash("raw-refresh"), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _revoker.Invocations.Should().BeEmpty("other devices stay signed in");
    }

    [Fact]
    public async Task Single_logout_blacklists_the_presented_access_token()
    {
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(
            new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(expires: DateTime.UtcNow.AddMinutes(5)));
        _http.Setup(h => h.GetHeaderValue("Authorization")).Returns($"Bearer {jwt}");

        await Logout().Handle(new LogoutCommand(), default);

        _blacklist.Verify(b => b.BlacklistTokenAsync(jwt, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Single_logout_without_a_sid_still_revokes_the_presented_refresh_token()
    {
        _sessions.Setup(s => s.GetCurrentSessionId()).Returns((Guid?)null); // legacy / impersonation token

        await Logout().Handle(new LogoutCommand("raw-refresh"), default);

        _repo.Verify(r => r.RevokeAsync(_user.Id, TokenHasher.Hash("raw-refresh"), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(s => s.RevokeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
