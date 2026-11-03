using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Features.DisableTwoFactor;
using Identity.Domains.Auth.Features.EnableTwoFactor;
using Identity.Domains.Auth.Features.RegenerateRecoveryCodes;
using Identity.Domains.Auth.Features.SetupTwoFactor;
using Identity.Domains.Auth.Features.VerifyTwoFactor;
using Identity.Domains.Users.Models;
using Identity.Services.TwoFactor;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Exceptions;

namespace Identity.Tests.TwoFactor;

/// <summary>An accepted authenticator code is single-use for the whole acceptance window (login, enable, disable, regenerate).</summary>
public sealed class TotpReplayTests : IAsyncLifetime
{
    private readonly TwoFactorTestHost _host = new(maxFailedAccessAttempts: 10);
    private ApplicationUser _user = null!;

    public async Task InitializeAsync() => _user = await _host.CreateUserAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<string> EnrollAsync(string? code = null)
    {
        var setup = await _host.Create<SetupTwoFactorHandler>().Handle(new SetupTwoFactorCommand(_user.Id), default);
        var key = setup.SharedKey.Replace(" ", string.Empty).ToUpperInvariant();
        await _host.Create<EnableTwoFactorHandler>()
            .Handle(new EnableTwoFactorCommand(_user.Id, code ?? Totp.Code(key)), default);
        return key;
    }

    private Task<LoginResult> VerifyAsync(string code)
    {
        var challenge = _host.Scope.GetRequiredService<ITwoFactorChallengeService>().Create(_user);
        return _host.Create<VerifyTwoFactorHandler>().Handle(new VerifyTwoFactorCommand(challenge.Token, code, null), default);
    }

    [Fact]
    public async Task A_login_code_used_twice_is_rejected_the_second_time_and_counts_as_a_strike()
    {
        var key = await EnrollAsync();
        var code = Totp.Code(key, 1);

        (await VerifyAsync(code)).AccessToken.Should().NotBeNull();

        await FluentActions.Awaiting(() => VerifyAsync(code))
            .Should().ThrowAsync<UnauthorizedException>().WithMessage(VerifyTwoFactorHandler.InvalidCodeMessage);
        (await _host.ReloadAsync(_user.Id)).AccessFailedCount.Should().Be(1);
    }

    [Fact]
    public async Task The_code_that_enabled_2fa_cannot_be_reused_to_log_in_disable_or_regenerate()
    {
        var key = await EnrollAsync();
        var enrollCode = Totp.Code(key);

        await FluentActions.Awaiting(() => VerifyAsync(enrollCode)).Should().ThrowAsync<UnauthorizedException>();
        await _host.Create<DisableTwoFactorHandler>()
            .Invoking(h => h.Handle(new DisableTwoFactorCommand(_user.Id, TwoFactorTestHost.Password, enrollCode), default))
            .Should().ThrowAsync<BadRequestException>();
        await _host.Create<RegenerateRecoveryCodesHandler>()
            .Invoking(h => h.Handle(new RegenerateRecoveryCodesCommand(_user.Id, enrollCode), default))
            .Should().ThrowAsync<BadRequestException>();

        (await _host.ReloadAsync(_user.Id)).TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_code_replayed_on_enable_is_rejected()
    {
        var setup = await _host.Create<SetupTwoFactorHandler>().Handle(new SetupTwoFactorCommand(_user.Id), default);
        var key = setup.SharedKey.Replace(" ", string.Empty).ToUpperInvariant();
        var code = Totp.Code(key);
        var twoFactor = _host.Scope.GetRequiredService<ITwoFactorService>();
        (await twoFactor.VerifyAuthenticatorCodeAsync(_user, code)).Should().BeTrue("first use (e.g. a sniffed request)");

        await _host.Create<EnableTwoFactorHandler>()
            .Invoking(h => h.Handle(new EnableTwoFactorCommand(_user.Id, code), default))
            .Should().ThrowAsync<BadRequestException>().WithMessage(EnableTwoFactorHandler.InvalidCodeMessage);
    }

    [Fact]
    public async Task A_wrong_code_does_not_burn_anything_and_the_next_steps_code_still_works()
    {
        var key = await EnrollAsync();
        var twoFactor = _host.Scope.GetRequiredService<ITwoFactorService>();

        (await twoFactor.VerifyAuthenticatorCodeAsync(_user, Totp.WrongCode(key))).Should().BeFalse();
        (await twoFactor.VerifyAuthenticatorCodeAsync(_user, Totp.Code(key, 1))).Should().BeTrue();
        (await twoFactor.VerifyAuthenticatorCodeAsync(_user, Totp.Code(key, 2))).Should().BeTrue();
    }

    [Fact]
    public async Task Parallel_use_of_the_same_code_succeeds_exactly_once()
    {
        var key = await EnrollAsync();
        var code = Totp.Code(key, 1);

        var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var scope = _host.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(_user.Id.ToString()))!;
            return await scope.ServiceProvider.GetRequiredService<ITwoFactorService>().VerifyAuthenticatorCodeAsync(user, code);
        }));

        (await Task.WhenAll(attempts)).Count(ok => ok).Should().Be(1);
    }

    [Fact]
    public async Task Distributed_cache_guard_is_atomic_per_process_and_scoped_per_user()
    {
        var guard = new DistributedCacheTotpReplayGuard(
            new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())));
        var userId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => guard.TryConsumeAsync(userId, "123456"))));

        results.Count(ok => ok).Should().Be(1);
        (await guard.TryConsumeAsync(Guid.NewGuid(), "123456")).Should().BeTrue("another user's identical code is independent");
        (await guard.TryConsumeAsync(userId, "654321")).Should().BeTrue();
    }

    [Fact]
    public void The_window_covers_the_providers_full_acceptance_range_and_keys_hold_no_plain_code()
    {
        // AuthenticatorTokenProvider accepts steps -2..+2 (5 x 30 s).
        TotpReplayGuard.Window.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(150));

        var userId = Guid.NewGuid();
        var key = TotpReplayGuard.KeyFor(userId, "123456");
        key.Should().StartWith(TotpReplayGuard.KeySegment + userId.ToString("N")).And.NotContain("123456");
        TotpReplayGuard.KeyFor(userId, "123456").Should().Be(key);
    }
}
