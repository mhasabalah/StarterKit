using Identity.Contracts.Auth.Commands;
using Identity.Domains.Audit.Models;
using Identity.Domains.Auth.Features.DisableTwoFactor;
using Identity.Domains.Auth.Features.EnableTwoFactor;
using Identity.Domains.Auth.Features.RegenerateRecoveryCodes;
using Identity.Domains.Auth.Features.SetupTwoFactor;
using Identity.Domains.Auth.Features.VerifyTwoFactor;
using Identity.Domains.Users.Models;
using Identity.Services.TwoFactor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shared.Core.Exceptions;

namespace Identity.Tests.TwoFactor;

/// <summary>
/// TOTP setup / enable / verify / disable / recovery codes against real ASP.NET Identity (RFC 6238 via the built-in
/// authenticator provider). Codes are produced from the stored key like an authenticator app would.
/// </summary>
public sealed class TwoFactorFlowTests : IAsyncLifetime
{
    private readonly TwoFactorTestHost _host = new(maxFailedAccessAttempts: 3);
    private ApplicationUser _user = null!;

    public async Task InitializeAsync() => _user = await _host.CreateUserAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<string> SetupAsync()
    {
        var result = await _host.Create<SetupTwoFactorHandler>().Handle(new SetupTwoFactorCommand(_user.Id), default);
        return result.SharedKey.Replace(" ", string.Empty).ToUpperInvariant();
    }

    private async Task<(string Key, EnableTwoFactorResult Result)> EnrollAsync()
    {
        var key = await SetupAsync();
        var result = await _host.Create<EnableTwoFactorHandler>()
            .Handle(new EnableTwoFactorCommand(_user.Id, Totp.Code(key)), default);
        return (key, result);
    }

    private Task<LoginResult> VerifyAsync(string? code = null, string? recoveryCode = null)
    {
        var challenge = _host.Scope.GetService<ITwoFactorChallengeService>()!.Create(_user);
        return _host.Create<VerifyTwoFactorHandler>()
            .Handle(new VerifyTwoFactorCommand(challenge.Token, code, recoveryCode), default);
    }

    [Fact]
    public async Task Setup_returns_an_otpauth_uri_and_stores_the_key_encrypted_but_does_not_enable_2fa()
    {
        var result = await _host.Create<SetupTwoFactorHandler>().Handle(new SetupTwoFactorCommand(_user.Id), default);
        var key = result.SharedKey.Replace(" ", string.Empty).ToUpperInvariant();

        result.AuthenticatorUri.Should().StartWith("otpauth://totp/").And.Contain($"secret={key}");
        var stored = await _host.RawTokenAsync(_user.Id, ProtectedUserStore.AuthenticatorKeyTokenName);
        stored.Should().NotBeNullOrEmpty().And.NotContain(key, "the key must not be stored in plain text");
        (await _host.Users.GetAuthenticatorKeyAsync(_user)).Should().Be(key, "the store decrypts it transparently");
        (await _host.ReloadAsync(_user.Id)).TwoFactorEnabled.Should().BeFalse();
        _host.Revoker.Invocations.Should().BeEmpty("setup must not end the current session");
    }

    [Fact]
    public async Task Enable_with_a_valid_code_turns_2fa_on_revokes_sessions_and_issues_an_mfa_session()
    {
        var (_, result) = await EnrollAsync();

        (await _host.ReloadAsync(_user.Id)).TwoFactorEnabled.Should().BeTrue();
        result.RecoveryCodes.Should().HaveCount(8).And.OnlyHaveUniqueItems();
        _host.Revoker.Verify(r => r.RevokeAllAsync(_user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _host.Sessions.Verify(s => s.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<string?>(), true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recovery_codes_are_stored_only_as_hashes()
    {
        var (_, result) = await EnrollAsync();

        var stored = await _host.RawTokenAsync(_user.Id, ProtectedUserStore.RecoveryCodeTokenName);
        stored.Should().NotBeNullOrEmpty();
        foreach (var code in result.RecoveryCodes)
        {
            stored.Should().NotContain(code);
            stored.Should().Contain(ProtectedUserStore.HashRecoveryCode(_user.Id, code));
        }

        (await _host.Users.CountRecoveryCodesAsync(_user)).Should().Be(8);
    }

    [Fact]
    public async Task Enable_with_a_wrong_code_keeps_2fa_off_and_is_not_a_lockout_strike()
    {
        var key = await SetupAsync();

        await _host.Create<EnableTwoFactorHandler>()
            .Invoking(h => h.Handle(new EnableTwoFactorCommand(_user.Id, Totp.WrongCode(key)), default))
            .Should().ThrowAsync<BadRequestException>();

        var reloaded = await _host.ReloadAsync(_user.Id);
        reloaded.TwoFactorEnabled.Should().BeFalse();
        reloaded.AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task Enable_without_setup_and_setup_when_already_enabled_are_rejected()
    {
        await _host.Create<EnableTwoFactorHandler>()
            .Invoking(h => h.Handle(new EnableTwoFactorCommand(_user.Id, "123456"), default))
            .Should().ThrowAsync<BadRequestException>();

        await EnrollAsync();

        await _host.Create<SetupTwoFactorHandler>()
            .Invoking(h => h.Handle(new SetupTwoFactorCommand(_user.Id), default))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Verify_with_a_valid_code_completes_the_login_as_mfa_and_resets_the_failure_count()
    {
        var (key, _) = await EnrollAsync();
        await _host.Users.AccessFailedAsync(_user);

        var result = await VerifyAsync(code: Totp.Code(key, 1));

        result.AccessToken.Should().Be("access");
        result.RequiresTwoFactor.Should().BeFalse();
        _host.Sessions.Verify(s => s.IssueAsync(It.IsAny<ApplicationUser>(), It.IsAny<string?>(), true,
            It.IsAny<CancellationToken>()), Times.Exactly(2), "enable + verify both issue MFA sessions");
        (await _host.ReloadAsync(_user.Id)).AccessFailedCount.Should().Be(0);
    }

    [Fact]
    public async Task Wrong_codes_are_lockout_strikes_and_lock_the_account()
    {
        var (key, _) = await EnrollAsync();

        await FluentActions.Awaiting(() => VerifyAsync(code: Totp.WrongCode(key)))
            .Should().ThrowAsync<UnauthorizedException>().WithMessage(VerifyTwoFactorHandler.InvalidCodeMessage);
        (await _host.ReloadAsync(_user.Id)).AccessFailedCount.Should().Be(1);

        await FluentActions.Awaiting(() => VerifyAsync(code: Totp.WrongCode(key))).Should().ThrowAsync<UnauthorizedException>();
        await FluentActions.Awaiting(() => VerifyAsync(code: Totp.WrongCode(key)))
            .Should().ThrowAsync<UnauthorizedException>().WithMessage(TwoFactorService.LockedOutMessage);

        // Locked: even the right code is refused now.
        await FluentActions.Awaiting(() => VerifyAsync(code: Totp.Code(key, 1)))
            .Should().ThrowAsync<UnauthorizedException>().WithMessage(TwoFactorService.LockedOutMessage);
        (await _host.Users.IsLockedOutAsync(await _host.ReloadAsync(_user.Id))).Should().BeTrue();
    }

    [Fact]
    public async Task A_recovery_code_works_exactly_once_and_its_use_is_audited()
    {
        var (_, enabled) = await EnrollAsync();
        var code = enabled.RecoveryCodes[0];

        var first = await VerifyAsync(recoveryCode: code.ToLowerInvariant());
        first.AccessToken.Should().NotBeNull();

        await FluentActions.Awaiting(() => VerifyAsync(recoveryCode: code)).Should().ThrowAsync<UnauthorizedException>();

        (await _host.Users.CountRecoveryCodesAsync(await _host.ReloadAsync(_user.Id))).Should().Be(7);
        var audit = await _host.Db.AdminAuditLogs.AsNoTracking()
            .SingleAsync(a => a.Action == IdentityAuditActions.TwoFactorRecoveryCodeUsed);
        audit.AdminId.Should().Be(_user.Id);
        audit.AfterJson.Should().NotContain(code);
    }

    [Fact]
    public async Task Verify_rejects_a_challenge_issued_before_the_security_stamp_changed()
    {
        var (key, _) = await EnrollAsync();
        var challenge = _host.Scope.GetService<ITwoFactorChallengeService>()!.Create(_user);
        await _host.Users.UpdateSecurityStampAsync(_user);

        await _host.Create<VerifyTwoFactorHandler>()
            .Invoking(h => h.Handle(new VerifyTwoFactorCommand(challenge.Token, Totp.Code(key, 1), null), default))
            .Should().ThrowAsync<UnauthorizedException>().WithMessage(VerifyTwoFactorHandler.InvalidChallengeMessage);
    }

    [Fact]
    public async Task Disable_requires_the_password_and_a_code()
    {
        var (key, _) = await EnrollAsync();

        await _host.Create<DisableTwoFactorHandler>()
            .Invoking(h => h.Handle(new DisableTwoFactorCommand(_user.Id, "wrong-password", Totp.Code(key, 1)), default))
            .Should().ThrowAsync<BadRequestException>();
        await _host.Create<DisableTwoFactorHandler>()
            .Invoking(h => h.Handle(new DisableTwoFactorCommand(_user.Id, TwoFactorTestHost.Password, Totp.WrongCode(key)), default))
            .Should().ThrowAsync<BadRequestException>();

        var stillOn = await _host.ReloadAsync(_user.Id);
        stillOn.TwoFactorEnabled.Should().BeTrue();
        stillOn.AccessFailedCount.Should().Be(2, "both failures are lockout strikes that survive the rejection");

        await _host.Create<DisableTwoFactorHandler>()
            .Handle(new DisableTwoFactorCommand(_user.Id, TwoFactorTestHost.Password, Totp.Code(key, 2)), default);

        var off = await _host.ReloadAsync(_user.Id);
        off.TwoFactorEnabled.Should().BeFalse();
        (await _host.RawTokenAsync(_user.Id, ProtectedUserStore.AuthenticatorKeyTokenName)).Should().BeNull();
        (await _host.RawTokenAsync(_user.Id, ProtectedUserStore.RecoveryCodeTokenName)).Should().BeNull();
        _host.Revoker.Verify(r => r.RevokeAllAsync(_user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Regenerating_recovery_codes_invalidates_the_old_ones()
    {
        var (key, enabled) = await EnrollAsync();

        var regenerated = await _host.Create<RegenerateRecoveryCodesHandler>()
            .Handle(new RegenerateRecoveryCodesCommand(_user.Id, Totp.Code(key, 1)), default);

        regenerated.RecoveryCodes.Should().NotIntersectWith(enabled.RecoveryCodes);
        await FluentActions.Awaiting(() => VerifyAsync(recoveryCode: enabled.RecoveryCodes[0]))
            .Should().ThrowAsync<UnauthorizedException>();
        (await VerifyAsync(recoveryCode: regenerated.RecoveryCodes[0])).AccessToken.Should().NotBeNull();
    }

    [Fact]
    public async Task Enable_and_disable_are_audited_without_secrets()
    {
        var (key, enabled) = await EnrollAsync();
        await _host.Create<DisableTwoFactorHandler>()
            .Handle(new DisableTwoFactorCommand(_user.Id, TwoFactorTestHost.Password, Totp.Code(key, 1)), default);

        var rows = await _host.Db.AdminAuditLogs.AsNoTracking().Where(a => a.TargetId == _user.Id).ToListAsync();

        rows.Select(r => r.Action).Should().Contain([IdentityAuditActions.TwoFactorEnabled, IdentityAuditActions.TwoFactorDisabled]);
        var json = string.Join("\n", rows.Select(r => r.BeforeJson + r.AfterJson));
        json.Should().NotContain(key).And.NotContain(TwoFactorTestHost.Password);
        foreach (var code in enabled.RecoveryCodes)
            json.Should().NotContain(code);
        json.Should().NotContainEquivalentOf("passwordHash").And.NotContainEquivalentOf("securityStamp");
    }
}
