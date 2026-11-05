using Identity.Contracts.Passkeys;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Passkeys.Exceptions;
using Identity.Domains.Passkeys.Features.MyPasskeys;
using Identity.Domains.Users.Models;
using Identity.Options;
using Identity.Services.Passkeys;
using Identity.Services.Security;
using Identity.Services.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Exceptions;

namespace Identity.Tests.Passkeys;

public class PasskeyCeremonyStoreTests
{
    private static readonly MemoryDistributedCache Cache =
        new(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

    private readonly DistributedCachePasskeyCeremonyStore _store =
        new(Cache, new Identity.Services.Sso.DistributedCacheOneTimeUseGuard(Cache));

    [Fact]
    public async Task A_ceremony_can_be_consumed_exactly_once()
    {
        var userId = Guid.NewGuid();
        var id = await _store.CreateAsync(
            new PasskeyCeremony(PasskeyCeremonyKind.Registration, userId, "state"), TimeSpan.FromMinutes(5));

        var first = await _store.ConsumeAsync(id, PasskeyCeremonyKind.Registration);
        var second = await _store.ConsumeAsync(id, PasskeyCeremonyKind.Registration);

        first.Should().Be(new PasskeyCeremony(PasskeyCeremonyKind.Registration, userId, "state"));
        second.Should().BeNull();
    }

    [Fact]
    public async Task A_sign_in_ceremony_cannot_complete_a_registration()
    {
        var id = await _store.CreateAsync(new PasskeyCeremony(PasskeyCeremonyKind.Assertion, null, "s"), TimeSpan.FromMinutes(5));

        (await _store.ConsumeAsync(id, PasskeyCeremonyKind.Registration)).Should().BeNull();
        // ...and the wrong-kind attempt burned it.
        (await _store.ConsumeAsync(id, PasskeyCeremonyKind.Assertion)).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-ceremony")]
    public async Task Unknown_or_empty_ids_find_nothing(string id) =>
        (await _store.ConsumeAsync(id, PasskeyCeremonyKind.Assertion)).Should().BeNull();

    [Fact]
    public async Task Oversized_ids_are_rejected_without_a_lookup() =>
        (await _store.ConsumeAsync(new string('a', 65), PasskeyCeremonyKind.Assertion)).Should().BeNull();

    [Fact]
    public async Task Ceremony_ids_are_unguessable_and_unique()
    {
        var ids = new HashSet<string>();
        for (var i = 0; i < 50; i++)
            ids.Add(await _store.CreateAsync(new PasskeyCeremony(PasskeyCeremonyKind.Assertion, null, "s"), TimeSpan.FromMinutes(1)));

        ids.Should().HaveCount(50).And.OnlyContain(id => id.Length >= 22);
    }
}

public class PasskeyServiceTests
{
    private readonly Mock<IPasskeyHandler<ApplicationUser>> _handler = new(MockBehavior.Strict);
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<IPasskeyCeremonyStore> _ceremonies = new();
    private readonly Mock<ISessionManager> _sessions = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly PasskeyOptions _options = new();
    private readonly ApplicationUser _user = Mocks.User();

    private PasskeyService Sut() => new(
        _handler.Object, _users.Object, _ceremonies.Object, _sessions.Object, _refreshTokens.Object,
        new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
        Microsoft.Extensions.Options.Options.Create(_options), _time, NullLogger<PasskeyService>.Instance);

    private void SessionSignedInAgo(TimeSpan age)
    {
        var sid = Guid.NewGuid();
        var now = _time.GetUtcNow().UtcDateTime;
        _sessions.Setup(s => s.GetCurrentSessionId()).Returns(sid);
        _refreshTokens.Setup(r => r.ListActiveFamiliesAsync(_user.Id, now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ActiveSession(sid, null, null, "127.0.0.1", now - age, now, now.AddDays(7), null)]);
    }

    [Fact]
    public async Task A_recent_sign_in_passes_the_step_up_check()
    {
        SessionSignedInAgo(TimeSpan.FromMinutes(5));
        await Sut().Invoking(s => s.EnsureFreshSessionAsync(_user.Id)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task An_old_session_must_reauthenticate_before_adding_a_passkey()
    {
        SessionSignedInAgo(TimeSpan.FromMinutes(16));
        var act = () => Sut().EnsureFreshSessionAsync(_user.Id);
        (await act.Should().ThrowAsync<PasskeyReauthenticationRequiredException>())
            .Which.ErrorCode.Should().Be("reauthentication_required");
    }

    [Fact]
    public async Task A_caller_without_a_known_session_must_reauthenticate()
    {
        _sessions.Setup(s => s.GetCurrentSessionId()).Returns((Guid?)null);
        await Sut().Invoking(s => s.EnsureFreshSessionAsync(_user.Id))
            .Should().ThrowAsync<PasskeyReauthenticationRequiredException>();

        _sessions.Setup(s => s.GetCurrentSessionId()).Returns(Guid.NewGuid()); // revoked / someone else's
        _refreshTokens.Setup(r => r.ListActiveFamiliesAsync(_user.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        await Sut().Invoking(s => s.EnsureFreshSessionAsync(_user.Id))
            .Should().ThrowAsync<PasskeyReauthenticationRequiredException>();
    }

    [Fact]
    public async Task Registration_is_refused_at_the_per_user_limit()
    {
        _options.MaxPasskeysPerUser = 1;
        _users.Setup(u => u.GetPasskeysAsync(_user)).ReturnsAsync([Passkey()]);

        var act = () => Sut().BeginRegistrationAsync(_user);

        (await act.Should().ThrowAsync<PasskeyLimitReachedException>()).Which.ErrorCode.Should().Be("passkey_limit_reached");
    }

    [Fact]
    public async Task A_ceremony_of_another_user_never_reaches_the_verifier()
    {
        _ceremonies.Setup(c => c.ConsumeAsync("c1", PasskeyCeremonyKind.Registration, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasskeyCeremony(PasskeyCeremonyKind.Registration, Guid.NewGuid(), "state"));

        var act = () => Sut().CompleteRegistrationAsync(_user, "c1", "{}", "name");

        await act.Should().ThrowAsync<InvalidPasskeyException>().WithMessage(PasskeyService.InvalidCeremonyMessage);
        _handler.VerifyNoOtherCalls(); // strict mock: PerformAttestationAsync was never called
    }

    [Fact]
    public async Task An_expired_sign_in_ceremony_is_unauthorized()
    {
        _ceremonies.Setup(c => c.ConsumeAsync("gone", PasskeyCeremonyKind.Assertion, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PasskeyCeremony?)null);

        await Sut().Invoking(s => s.CompleteAssertionAsync("gone", "{}")).Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Disabled_passkeys_answer_404_with_a_stable_code()
    {
        _options.Enabled = false;
        var act = () => Sut().BeginAssertionAsync();
        (await act.Should().ThrowAsync<PasskeysDisabledException>()).Which.ErrorCode.Should().Be("passkeys_disabled");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" Work laptop ", "Work laptop")]
    public void Names_are_trimmed_and_blank_means_none(string? input, string? expected) =>
        PasskeyService.NormalizeName(input).Should().Be(expected);

    [Fact]
    public void Credential_ids_round_trip_and_garbage_does_not_throw()
    {
        var raw = new byte[] { 1, 2, 3, 250, 251, 252 };
        PasskeyService.DecodeId(PasskeyService.EncodeId(raw)).Should().Equal(raw);
        PasskeyService.DecodeId("***not base64***").Should().BeNull();
        PasskeyService.DecodeId(new string('A', 2000)).Should().BeNull();
    }

    internal static UserPasskeyInfo Passkey(byte id = 1) =>
        new([id], [9], DateTimeOffset.UnixEpoch, 0, ["internal"], true, true, true, [0], [0]);
}

public class PasskeyConfigurationTests
{
    [Fact]
    public async Task Maps_the_relying_party_and_requires_user_verification()
    {
        var target = new IdentityPasskeyOptions();
        PasskeyConfiguration.Apply(target, new PasskeyOptions
        {
            ServerDomain = " example.com ",
            AllowedOrigins = ["https://app.example.com/", " "]
        });

        target.ServerDomain.Should().Be("example.com");
        target.UserVerificationRequirement.Should().Be("required");
        target.ResidentKeyRequirement.Should().Be("required");
        target.AttestationConveyancePreference.Should().Be("none");

        (await Validate(target, "https://app.example.com")).Should().BeTrue();
        (await Validate(target, "https://evil.example.com")).Should().BeFalse();
        (await Validate(target, "https://app.example.com", crossOrigin: true)).Should().BeFalse();
    }

    private static async Task<bool> Validate(IdentityPasskeyOptions options, string origin, bool crossOrigin = false) =>
        await options.ValidateOrigin!(new PasskeyOriginValidationContext
        {
            HttpContext = new DefaultHttpContext(),
            Origin = origin,
            CrossOrigin = crossOrigin
        });
}

public class DeletePasskeyHandlerTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = Mocks.UserManager();
    private readonly Mock<IPasskeyService> _passkeys = new();
    private readonly RecordingAuditWriter _audit = new();
    private readonly Mock<ISecurityAlertSender> _alerts = new();
    private readonly ApplicationUser _user = Mocks.User();

    public DeletePasskeyHandlerTests()
    {
        _users.WithUsers((_user, ["User"]));
        _users.Setup(u => u.GetPasskeyAsync(_user, It.IsAny<byte[]>())).ReturnsAsync(PasskeyServiceTests.Passkey());
        _users.Setup(u => u.GetPasskeysAsync(_user)).ReturnsAsync([PasskeyServiceTests.Passkey()]);
        _users.Setup(u => u.GetLoginsAsync(_user)).ReturnsAsync([]);
        _users.Setup(u => u.RemovePasskeyAsync(_user, It.IsAny<byte[]>())).ReturnsAsync(IdentityResult.Success);
    }

    private DeletePasskeyHandler Sut() => new(_users.Object, _passkeys.Object, _audit, _alerts.Object);

    private static string Id => PasskeyService.EncodeId([1]);

    [Fact]
    public async Task The_only_way_in_cannot_be_removed()
    {
        _users.Setup(u => u.HasPasswordAsync(_user)).ReturnsAsync(false);

        var act = () => Sut().Handle(new DeletePasskeyCommand(_user.Id, Id), CancellationToken.None);

        (await act.Should().ThrowAsync<LastSignInMethodException>()).Which.ErrorCode.Should().Be("last_sign_in_method");
        _users.Verify(u => u.RemovePasskeyAsync(It.IsAny<ApplicationUser>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task With_a_password_left_the_passkey_is_removed_audited_and_announced()
    {
        _users.Setup(u => u.HasPasswordAsync(_user)).ReturnsAsync(true);

        var result = await Sut().Handle(new DeletePasskeyCommand(_user.Id, Id), CancellationToken.None);

        result.Deleted.Should().BeTrue();
        _audit.Entries.Should().ContainSingle(e => e.Action == "passkey.removed" && e.TargetId == _user.Id);
        _alerts.Verify(a => a.SendAsync(_user, "passkey_removed", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_unknown_or_foreign_passkey_is_not_found()
    {
        _users.Setup(u => u.GetPasskeyAsync(_user, It.IsAny<byte[]>())).ReturnsAsync((UserPasskeyInfo?)null);

        await Sut().Invoking(s => s.Handle(new DeletePasskeyCommand(_user.Id, Id), CancellationToken.None))
            .Should().ThrowAsync<PasskeyNotFoundException>();
    }
}
