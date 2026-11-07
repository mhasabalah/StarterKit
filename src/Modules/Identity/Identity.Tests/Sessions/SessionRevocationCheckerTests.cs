using FluentAssertions;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Moq;
using Xunit;

namespace Identity.Tests.Sessions;

public class SessionRevocationCheckerTests
{
    private readonly Mock<ITokenBlacklistService> _blacklist = new();

    private SessionRevocationChecker Checker() => new(_blacklist.Object);

    [Fact]
    public async Task A_blacklisted_session_is_revoked()
    {
        var user = Guid.NewGuid();
        var sid = Guid.NewGuid();
        _blacklist.Setup(b => b.IsSessionRevokedAsync(sid)).ReturnsAsync(true);

        (await Checker().IsRevokedAsync(user, sid, "stamp")).Should().BeTrue();
    }

    [Fact]
    public async Task A_token_with_a_stale_security_stamp_is_revoked_even_if_its_session_is_not_blacklisted()
    {
        var user = Guid.NewGuid();
        var sid = Guid.NewGuid();
        _blacklist.Setup(b => b.IsSessionRevokedAsync(sid)).ReturnsAsync(false);
        _blacklist.Setup(b => b.IsUserTokenRevokedAsync(user, "old")).ReturnsAsync(true);

        (await Checker().IsRevokedAsync(user, sid, "old")).Should().BeTrue("revoke-all rotates the stamp");
    }

    [Fact]
    public async Task A_live_token_without_a_session_id_is_not_revoked()
    {
        var user = Guid.NewGuid();
        _blacklist.Setup(b => b.IsUserTokenRevokedAsync(user, null)).ReturnsAsync(false);

        (await Checker().IsRevokedAsync(user, null, null)).Should().BeFalse();
        _blacklist.Verify(b => b.IsSessionRevokedAsync(It.IsAny<Guid>()), Times.Never);
    }
}
