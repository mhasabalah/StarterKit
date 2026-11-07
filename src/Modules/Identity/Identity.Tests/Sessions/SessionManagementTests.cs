using Identity.Contracts.Sessions;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Sessions.Exceptions;
using Identity.Domains.Sessions.Features.MySessions;
using Identity.Domains.Sessions.Features.UserSessions;
using Identity.Domains.Users.Models;
using Identity.Models;
using Identity.Options;
using Identity.Resources;
using Identity.Services.Sessions;
using Identity.Services.Token;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.Exceptions;
using Shared.Core.Web;

namespace Identity.Tests.Sessions;

public sealed class DeviceNameParserTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36", "Chrome on Windows")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36 Edg/126.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/17.0 Safari/605.1.15", "Safari on macOS")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile Safari/604.1", "Safari on iOS")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:127.0) Gecko/20100101 Firefox/127.0", "Firefox on Linux")]
    [InlineData("okhttp/4.12.0", "Android app")]
    public void Parse_gives_a_recognisable_label(string userAgent, string expected) =>
        DeviceNameParser.Parse(userAgent).Should().Be(expected);

    [Fact]
    public void Parse_of_nothing_is_null_and_unknown_agents_fall_back_to_their_product_token()
    {
        DeviceNameParser.Parse(null).Should().BeNull();
        DeviceNameParser.Parse("  ").Should().BeNull();
        DeviceNameParser.Parse("MyCustomClient/2.1").Should().Be("MyCustomClient");
    }

    [Fact]
    public void Truncate_caps_the_stored_user_agent()
    {
        DeviceNameParser.Truncate(new string('a', 2000))!.Length.Should().Be(DeviceNameParser.MaxUserAgentLength);
    }
}

public sealed class SessionManagementTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<IRefreshTokenRepository> _repo = new();
    private readonly Mock<ITokenBlacklistService> _blacklist = new();
    private readonly Mock<ISessionRevocationRecorder> _revocations = new();
    private readonly Mock<IHttpContextService> _http = new();
    private readonly Mock<IJwtTokenGenerator> _jwt = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ApplicationUser _user = Mocks.User();

    private static readonly JwtOptions Jwt = new()
    {
        SecretKey = new string('k', 40), Issuer = "i", Audience = "a",
        AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 7
    };

    public SessionManagementTests()
    {
        _users.WithUsers((_user, [RoleConstants.User]));
        _http.Setup(h => h.GetUserIdAsGuid()).Returns(_user.Id);
        _http.Setup(h => h.GetIpAddress()).Returns("9.9.9.9");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString("N"));
        _jwt.Setup(j => j.GenerateTokenAsync(
                It.IsAny<ApplicationUser>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<bool>(),
                It.IsAny<Shared.Core.MultiTenancy.TenantMembershipInfo?>(), It.IsAny<ImpersonationGrant?>(), It.IsAny<Guid?>()))
            .ReturnsAsync("access");
        _repo.Setup(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private SessionManager Manager() => new(_repo.Object, _blacklist.Object, _revocations.Object, _http.Object,
        Microsoft.Extensions.Options.Options.Create(Jwt), _time, NullLogger<SessionManager>.Instance);

    private AuthSessionService AuthSessions() => new(_users.Object, _jwt.Object, _repo.Object,
        Microsoft.Extensions.Options.Options.Create(Jwt), _time, NullLogger<AuthSessionService>.Instance,
        httpContext: _http.Object);

    // ---- session metadata + sid -------------------------------------------------------------------------------

    [Fact]
    public async Task Login_records_user_agent_device_last_used_and_puts_the_family_id_in_the_access_token_as_sid()
    {
        const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36";
        _http.Setup(h => h.GetUserAgent()).Returns(ua);
        RefreshToken? stored = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => stored = t);

        await AuthSessions().IssueAsync(_user, "1.2.3.4");

        stored!.UserAgent.Should().Be(ua);
        stored.DeviceName.Should().Be("Chrome on Windows");
        stored.LastUsedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
        _jwt.Verify(j => j.GenerateTokenAsync(
            _user, It.IsAny<IReadOnlyCollection<string>>(), false, null, null, stored.FamilyId), Times.Once);
    }

    [Fact]
    public async Task Rotation_keeps_the_device_and_the_sid_and_advances_last_used()
    {
        RefreshToken? first = null, rotated = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => first = t);
        _repo.Setup(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, RefreshToken, string?, CancellationToken>((_, t, _, _) => rotated = t)
            .ReturnsAsync(true);
        _http.Setup(h => h.GetUserAgent()).Returns("Mozilla/5.0 (X11; Linux x86_64; rv:127.0) Gecko/20100101 Firefox/127.0");

        var service = AuthSessions();
        var session = await service.IssueAsync(_user, "1.2.3.4");
        _repo.Setup(r => r.FindByHashAsync(TokenHasher.Hash(session.RefreshToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);

        // The refresh call comes from somewhere else; the session still belongs to the original device.
        _http.Setup(h => h.GetUserAgent()).Returns("curl/8");
        _time.Advance(TimeSpan.FromMinutes(5));
        await service.RefreshAsync(session.RefreshToken, "5.6.7.8");

        rotated!.FamilyId.Should().Be(first!.FamilyId);
        rotated.DeviceName.Should().Be("Firefox on Linux");
        rotated.LastUsedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
        _jwt.Verify(j => j.GenerateTokenAsync(
            _user, It.IsAny<IReadOnlyCollection<string>>(), false, null, null, first.FamilyId), Times.Exactly(2));
    }

    // ---- SessionManager ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Revoking_a_session_revokes_its_family_and_blacklists_the_sid_for_the_access_token_lifetime()
    {
        var sid = Guid.NewGuid();
        _repo.Setup(r => r.RevokeUserFamilyAsync(_user.Id, sid, It.IsAny<string>(), "9.9.9.9", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var revoked = await Manager().RevokeAsync(_user.Id, sid, "because");

        revoked.Should().BeTrue();
        _blacklist.Verify(b => b.BlacklistSessionAsync(sid, TimeSpan.FromMinutes(16)), Times.Once);
        _revocations.Verify(r => r.RecordSessionsAsync(_user.Id,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == sid), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoking_an_unknown_or_foreign_session_does_nothing_and_blacklists_nothing()
    {
        _repo.Setup(r => r.RevokeUserFamilyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        (await Manager().RevokeAsync(_user.Id, Guid.NewGuid(), "x")).Should().BeFalse();
        _revocations.Verify(r => r.RecordSessionsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _blacklist.Verify(b => b.BlacklistSessionAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task A_redis_outage_does_not_undo_the_database_revocation()
    {
        _repo.Setup(r => r.RevokeUserFamilyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _blacklist.Setup(b => b.BlacklistSessionAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>()))
            .ThrowsAsync(new TimeoutException("redis down"));

        (await Manager().RevokeAsync(_user.Id, Guid.NewGuid(), "x")).Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_others_blacklists_every_ended_session_but_not_the_current_one()
    {
        var current = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _repo.Setup(r => r.RevokeOtherFamiliesAsync(_user.Id, current, It.IsAny<string>(), "9.9.9.9",
                _time.GetUtcNow().UtcDateTime, It.IsAny<CancellationToken>()))
            .ReturnsAsync([a, b]);

        var count = await Manager().RevokeOthersAsync(_user.Id, current, "x");

        count.Should().Be(2);
        _blacklist.Verify(x => x.BlacklistSessionAsync(a, It.IsAny<TimeSpan>()), Times.Once);
        _blacklist.Verify(x => x.BlacklistSessionAsync(b, It.IsAny<TimeSpan>()), Times.Once);
        _blacklist.Verify(x => x.BlacklistSessionAsync(current, It.IsAny<TimeSpan>()), Times.Never);
        _revocations.Verify(r => r.RecordSessionsAsync(_user.Id,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { a, b })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Current_session_id_is_read_from_the_sid_claim_and_absent_for_tokens_without_one()
    {
        var sid = Guid.NewGuid();
        _http.Setup(h => h.GetClaimValue("sid")).Returns(sid.ToString());
        Manager().GetCurrentSessionId().Should().Be(sid);

        _http.Setup(h => h.GetClaimValue("sid")).Returns((string?)null);
        Manager().GetCurrentSessionId().Should().BeNull();
    }

    // ---- handlers ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task My_sessions_flags_the_current_one_and_lists_only_my_active_sessions()
    {
        var current = Guid.NewGuid();
        var other = Guid.NewGuid();
        var now = _time.GetUtcNow().UtcDateTime;
        _repo.Setup(r => r.ListActiveFamiliesAsync(_user.Id, now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ActiveSession(current, "Chrome on Windows", "ua", "1.1.1.1", now.AddDays(-1), now, now.AddDays(6), null),
                new ActiveSession(other, null, null, "2.2.2.2", now.AddDays(-2), now.AddHours(-3), now.AddDays(5), null)
            ]);
        var sessions = new Mock<ISessionManager>();
        sessions.Setup(s => s.GetCurrentSessionId()).Returns(current);

        var result = await new GetMySessionsHandler(_repo.Object, sessions.Object, _http.Object, _time)
            .Handle(new GetMySessionsQuery(), default);

        result.Should().HaveCount(2);
        result.Single(s => s.Id == current).IsCurrent.Should().BeTrue();
        result.Single(s => s.Id == other).IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Revoking_someone_elses_session_looks_like_not_found()
    {
        var sessions = new Mock<ISessionManager>();
        sessions.Setup(s => s.RevokeAsync(_user.Id, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => new RevokeMySessionHandler(sessions.Object, _http.Object)
            .Handle(new RevokeMySessionCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<SessionNotFoundException>();
    }

    [Fact]
    public async Task Revoke_others_keeps_the_current_session()
    {
        var current = Guid.NewGuid();
        var sessions = new Mock<ISessionManager>();
        sessions.Setup(s => s.GetCurrentSessionId()).Returns(current);
        sessions.Setup(s => s.RevokeOthersAsync(_user.Id, current, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await new RevokeOtherSessionsHandler(sessions.Object, _http.Object)
            .Handle(new RevokeOtherSessionsCommand(), default);

        result.Revoked.Should().Be(3);
    }

    [Fact]
    public async Task Handlers_reject_unauthenticated_callers()
    {
        _http.Setup(h => h.GetUserIdAsGuid()).Returns((Guid?)null);
        var sessions = new Mock<ISessionManager>();

        await new RevokeMySessionHandler(sessions.Object, _http.Object)
            .Invoking(h => h.Handle(new RevokeMySessionCommand(Guid.NewGuid()), default))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    private RevokeUserSessionHandler AdminHandler(Guid callerId, string[] callerRoles, ApplicationUser target,
        string[] targetRoles, Mock<ISessionManager> sessions, RecordingAuditWriter audit)
    {
        _users.WithUsers((target, targetRoles));
        var http = new Mock<IHttpContextService>();
        http.Setup(h => h.GetUserIdAsGuid()).Returns(callerId);
        http.Setup(h => h.GetRoles()).Returns(callerRoles);
        return new RevokeUserSessionHandler(_users.Object, sessions.Object, audit, http.Object,
            new KeyLocalizer<IdentityResource>());
    }

    [Fact]
    public async Task Support_revoking_a_customer_session_succeeds_and_is_audited_with_actor_and_target()
    {
        var support = Guid.NewGuid();
        var customer = Mocks.User();
        var sid = Guid.NewGuid();
        var sessions = new Mock<ISessionManager>();
        sessions.Setup(s => s.RevokeAsync(customer.Id, sid, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var audit = new RecordingAuditWriter();

        await AdminHandler(support, [RoleConstants.Support], customer, [RoleConstants.User], sessions, audit)
            .Handle(new RevokeUserSessionCommand(customer.Id, sid), default);

        audit.Entries.Should().ContainSingle(e =>
            e.Action == "session.revoked" && e.TargetId == customer.Id && e.ActorId == support);
    }

    [Fact]
    public async Task Support_cannot_touch_the_sessions_of_a_staff_account_but_a_super_admin_can()
    {
        var admin = Mocks.User();
        var sid = Guid.NewGuid();
        var sessions = new Mock<ISessionManager>();
        sessions.Setup(s => s.RevokeAsync(admin.Id, sid, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var audit = new RecordingAuditWriter();

        await AdminHandler(Guid.NewGuid(), [RoleConstants.Support], admin, [RoleConstants.Admin], sessions, audit)
            .Invoking(h => h.Handle(new RevokeUserSessionCommand(admin.Id, sid), default))
            .Should().ThrowAsync<ForbiddenException>();
        sessions.Verify(s => s.RevokeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        audit.Entries.Should().BeEmpty();

        await AdminHandler(Guid.NewGuid(), [RoleConstants.SuperAdmin], admin, [RoleConstants.Admin], sessions, audit)
            .Handle(new RevokeUserSessionCommand(admin.Id, sid), default);
        audit.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task Admin_revoking_an_unknown_session_is_not_found_and_not_audited()
    {
        var customer = Mocks.User();
        var sessions = new Mock<ISessionManager>();
        var audit = new RecordingAuditWriter();

        await AdminHandler(Guid.NewGuid(), [RoleConstants.Support], customer, [RoleConstants.User], sessions, audit)
            .Invoking(h => h.Handle(new RevokeUserSessionCommand(customer.Id, Guid.NewGuid()), default))
            .Should().ThrowAsync<SessionNotFoundException>();
        audit.Entries.Should().BeEmpty();
    }

    // ---- sid blacklist on the JWT pipeline --------------------------------------------------------------------

    [Fact]
    public async Task A_valid_token_of_a_revoked_session_is_rejected_by_the_jwt_events()
    {
        var sid = Guid.NewGuid();
        _blacklist.Setup(b => b.IsSessionRevokedAsync(sid)).ReturnsAsync(true);
        var context = TokenContext(new Claim("sid", sid.ToString()), new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()));

        await new TokenBlacklistJwtBearerEvents(_blacklist.Object, NullLogger<TokenBlacklistJwtBearerEvents>.Instance)
            .TokenValidated(context);

        context.Result?.Failure.Should().NotBeNull("the access token of a revoked session must stop working immediately");
    }

    [Fact]
    public async Task A_token_of_a_live_session_or_without_sid_still_passes()
    {
        var live = Guid.NewGuid();
        _blacklist.Setup(b => b.IsSessionRevokedAsync(live)).ReturnsAsync(false);
        var events = new TokenBlacklistJwtBearerEvents(_blacklist.Object, NullLogger<TokenBlacklistJwtBearerEvents>.Instance);

        var withSid = TokenContext(new Claim("sid", live.ToString()), new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()));
        await events.TokenValidated(withSid);
        withSid.Result?.Failure.Should().BeNull();

        var legacy = TokenContext(new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()));
        await events.TokenValidated(legacy);
        legacy.Result?.Failure.Should().BeNull();
    }

    private static TokenValidatedContext TokenContext(params Claim[] claims)
    {
        var http = new DefaultHttpContext();
        var scheme = new Microsoft.AspNetCore.Authentication.AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(http, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
        };
    }
}
