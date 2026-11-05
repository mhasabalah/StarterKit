using Identity.Contracts.Admins.Commands;
using Identity.Contracts.Auth.Commands;
using Identity.Domains.Users.Models;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Identity.Services.TwoFactor;
using Shared.Contracts.Mediation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Exceptions;
using StackExchange.Redis;
using Testcontainers.Redis;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;
using Totp = Identity.Tests.Totp;

namespace Identity.IntegrationTests;

/// <summary>
/// Single-use second factors under concurrency (PostgreSQL concurrency stamp for recovery codes) and immediate
/// effect of role grants on live sessions.
/// </summary>
[Collection(IdentityPostgresCollection.Name)]
public sealed class ReplayAndRoleChangeTests(IdentityPostgresFixture fixture)
{
    private async Task<T> SendAsync<T>(IRequest<T> request)
    {
        await using var scope = fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private async Task<AuthSession> LoginAsync(Guid userId)
    {
        await using var scope = fixture.NewScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId.ToString());
        return await scope.ServiceProvider.GetRequiredService<IAuthSessionService>().IssueAsync(user!, "127.0.0.1");
    }

    [Fact]
    public async Task Parallel_redemption_of_one_recovery_code_succeeds_exactly_once()
    {
        var user = await fixture.CreateUserAsync();
        var setup = await SendAsync(new SetupTwoFactorCommand(user.Id));
        var key = setup.SharedKey.Replace(" ", string.Empty).ToUpperInvariant();
        var enabled = await SendAsync(new EnableTwoFactorCommand(user.Id, Totp.Code(key)));
        var code = enabled.RecoveryCodes[0];

        var attempts = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await using var scope = fixture.NewScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var loaded = await users.FindByIdAsync(user.Id.ToString());
            try
            {
                return (await users.RedeemTwoFactorRecoveryCodeAsync(loaded!, code)).Succeeded;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false; // a concurrency conflict surfaced as an exception is still a refusal
            }
        }));

        (await Task.WhenAll(attempts)).Count(ok => ok).Should().Be(1, "the concurrency stamp serializes the token update");

        await using var verify = fixture.NewScope();
        var manager = verify.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await manager.CountRecoveryCodesAsync((await manager.FindByIdAsync(user.Id.ToString()))!))
            .Should().Be(enabled.RecoveryCodes.Count - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Granting_a_role_ends_existing_sessions_so_the_new_role_applies_immediately(bool promote)
    {
        var superAdmin = await fixture.CreateUserAsync(null, RoleConstants.User, RoleConstants.Admin, RoleConstants.SuperAdmin);
        var target = await fixture.CreateUserAsync();
        var before = await LoginAsync(target.Id);
        fixture.CurrentUserId = superAdmin.Id;

        if (promote)
        {
            await SendAsync(new PromoteToAdminCommand
            {
                UserId = target.Id, Reason = "integration test promotion", PromotedByAdminId = superAdmin.Id
            });
        }
        else
        {
            await SendAsync(new AssignRoleCommand
            {
                UserId = target.Id, RoleName = RoleConstants.Finance, AssignedByAdminId = superAdmin.Id
            });
        }

        // The old refresh token (bound to the old stamp) is dead...
        await using (var scope = fixture.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuthSessionService>()
                .Invoking(s => s.RefreshAsync(before.RefreshToken, "127.0.0.1"))
                .Should().ThrowAsync<UnauthorizedException>();
        }

        // ...and so is the old access token (stamp-hash revocation checked by the JWT bearer events).
        var oldStampHash = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(before.AccessToken)
            .Claims.Single(c => c.Type == IdentityClaimTypes.SecurityStampHash).Value;
        (await fixture.Services.GetRequiredService<ITokenBlacklistService>().IsUserTokenRevokedAsync(target.Id, oldStampHash))
            .Should().BeTrue();

        // A fresh login carries the new role.
        var after = await LoginAsync(target.Id);
        var roles = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(after.AccessToken)
            .Claims.Select(c => c.Value);
        roles.Should().Contain(promote ? RoleConstants.Admin : RoleConstants.Finance);
    }
}

/// <summary>The Redis replay guard against a real Redis: SET NX is atomic across connections (= API instances).</summary>
public sealed class RedisTotpReplayGuardTests : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();
    private IConnectionMultiplexer _first = null!;
    private IConnectionMultiplexer _second = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _first = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        _second = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await _first.DisposeAsync();
        await _second.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Parallel_use_across_instances_succeeds_exactly_once_and_expires_with_the_window()
    {
        var instances = new ITotpReplayGuard[] { new RedisTotpReplayGuard(_first, "sk:"), new RedisTotpReplayGuard(_second, "sk:") };
        var userId = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => instances[i % 2].TryConsumeAsync(userId, "123456"))));

        results.Count(ok => ok).Should().Be(1);
        (await instances[0].TryConsumeAsync(Guid.NewGuid(), "123456")).Should().BeTrue("per-user");

        var ttl = await _first.GetDatabase().KeyTimeToLiveAsync("sk:" + TotpReplayGuard.KeyFor(userId, "123456"));
        ttl.Should().NotBeNull();
        ttl!.Value.Should().BeGreaterThan(TimeSpan.FromSeconds(150)).And.BeLessThanOrEqualTo(TotpReplayGuard.Window);
    }
}
