using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Models;
using Identity.Models;
using Identity.Options;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.Exceptions;

namespace Identity.Tests.Sessions;

public sealed class AuthSessionServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<IJwtTokenGenerator> _jwt = new();
    private readonly Mock<IRefreshTokenRepository> _repo = new();
    private readonly Mock<ISessionRevocationRecorder> _revocations = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly ApplicationUser _user = Mocks.User();

    public AuthSessionServiceTests()
    {
        _users.WithUsers((_user, [RoleConstants.User]));
        _jwt.Setup(j => j.GenerateTokenAsync(It.IsAny<ApplicationUser>(), It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync("access-token");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString("N"));
        _repo.Setup(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private AuthSessionService Service() => new(
        _users.Object, _jwt.Object, _repo.Object,
        Microsoft.Extensions.Options.Options.Create(new JwtOptions
        {
            SecretKey = new string('k', 40), Issuer = "i", Audience = "a",
            AccessTokenExpirationMinutes = 15, RefreshTokenExpirationDays = 7
        }),
        _time, NullLogger<AuthSessionService>.Instance, revocations: _revocations.Object);

    private RefreshToken StoredToken(string raw, Action<RefreshToken>? configure = null)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = _user.Id,
            FamilyId = Guid.NewGuid(),
            TokenHash = TokenHasher.Hash(raw),
            SecurityStampHash = TokenHasher.HashSecurityStamp(_user.SecurityStamp),
            CreatedAt = _time.GetUtcNow().UtcDateTime,
            ExpiresAt = _time.GetUtcNow().UtcDateTime.AddDays(1)
        };
        configure?.Invoke(token);
        _repo.Setup(r => r.FindByHashAsync(token.TokenHash, It.IsAny<CancellationToken>())).ReturnsAsync(token);
        return token;
    }

    [Fact]
    public async Task Issue_persists_a_hashed_refresh_token_bound_to_a_new_family_and_the_security_stamp()
    {
        RefreshToken? saved = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => saved = t);

        var session = await Service().IssueAsync(_user, "1.2.3.4");

        saved.Should().NotBeNull();
        saved!.TokenHash.Should().Be(TokenHasher.Hash(session.RefreshToken)).And.NotBe(session.RefreshToken);
        saved.FamilyId.Should().NotBeEmpty();
        saved.SecurityStampHash.Should().Be(TokenHasher.HashSecurityStamp(_user.SecurityStamp));
        saved.ExpiresAt.Should().Be(_time.GetUtcNow().UtcDateTime.AddDays(7));
        session.Roles.Should().Equal(RoleConstants.User);
    }

    [Fact]
    public async Task Refresh_rotates_within_the_same_family()
    {
        var current = StoredToken("raw");
        RefreshToken? replacement = null;
        _repo.Setup(r => r.TryRotateAsync(current.Id, It.IsAny<RefreshToken>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, RefreshToken, string?, CancellationToken>((_, t, _, _) => replacement = t)
            .ReturnsAsync(true);

        var session = await Service().RefreshAsync("raw", "ip");

        replacement!.FamilyId.Should().Be(current.FamilyId);
        replacement.TokenHash.Should().Be(TokenHasher.Hash(session.RefreshToken));
        _repo.Verify(r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reusing_a_revoked_token_revokes_the_whole_family()
    {
        var current = StoredToken("raw", t =>
        {
            t.RevokedAt = DateTime.UtcNow;
            t.RevokedReason = RefreshToken.RotatedReason;
            t.ReplacedByTokenId = Guid.NewGuid();
        });

        await Service().Invoking(s => s.RefreshAsync("raw", "ip")).Should().ThrowAsync<UnauthorizedException>();

        _repo.Verify(r => r.RevokeFamilyAsync(current.FamilyId, AuthSessionService.ReuseDetectedReason,
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reusing_a_revoked_token_announces_the_ended_session_so_live_connections_close()
    {
        var current = StoredToken("raw", t =>
        {
            t.RevokedAt = DateTime.UtcNow;
            t.RevokedReason = RefreshToken.RotatedReason;
        });
        _repo.Setup(r => r.RevokeFamilyAsync(current.FamilyId, It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(2);

        await Service().Invoking(s => s.RefreshAsync("raw", "ip")).Should().ThrowAsync<UnauthorizedException>();

        _revocations.Verify(r => r.RecordSessionsAsync(_user.Id,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == current.FamilyId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Losing_a_concurrent_rotation_is_treated_as_reuse()
    {
        var current = StoredToken("raw");
        _repo.Setup(r => r.TryRotateAsync(current.Id, It.IsAny<RefreshToken>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await Service().Invoking(s => s.RefreshAsync("raw", "ip")).Should().ThrowAsync<UnauthorizedException>();

        _repo.Verify(r => r.RevokeFamilyAsync(current.FamilyId, It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_and_expired_tokens_are_rejected()
    {
        StoredToken("expired", t => t.ExpiresAt = _time.GetUtcNow().UtcDateTime.AddSeconds(-1));

        await Service().Invoking(s => s.RefreshAsync("unknown", null)).Should().ThrowAsync<UnauthorizedException>();
        await Service().Invoking(s => s.RefreshAsync("expired", null)).Should().ThrowAsync<UnauthorizedException>();
    }

    public static TheoryData<string> UserStates => new() { "deleted", "inactive", "locked", "stamp" };

    [Theory]
    [MemberData(nameof(UserStates))]
    public async Task Refresh_is_rejected_for_deleted_inactive_locked_or_restamped_users(string state)
    {
        var current = StoredToken("raw");
        switch (state)
        {
            case "deleted":
                _users.Setup(m => m.FindByIdAsync(_user.Id.ToString())).ReturnsAsync((ApplicationUser?)null);
                break;
            case "inactive":
                _user.IsActive = false;
                break;
            case "locked":
                _users.Setup(m => m.IsLockedOutAsync(_user)).ReturnsAsync(true);
                break;
            case "stamp":
                _user.SecurityStamp = Guid.NewGuid().ToString();
                break;
        }

        await Service().Invoking(s => s.RefreshAsync("raw", null)).Should().ThrowAsync<UnauthorizedException>();

        _repo.Verify(r => r.RevokeFamilyAsync(current.FamilyId, It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<RefreshToken>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
