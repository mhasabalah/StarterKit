using Identity.Data;
using Identity.Infrastructure.BackgroundJobs;
using Identity.Models;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Users.Models;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Exceptions;

namespace Identity.IntegrationTests;

[Collection(IdentityPostgresCollection.Name)]
public sealed class RefreshTokenTests(IdentityPostgresFixture fixture)
{
    private async Task<AuthSession> LoginAsync(ApplicationUser user)
    {
        await using var scope = fixture.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tracked = await users.FindByIdAsync(user.Id.ToString());
        return await scope.ServiceProvider.GetRequiredService<IAuthSessionService>().IssueAsync(tracked!, "127.0.0.1");
    }

    private async Task<AuthSession> RefreshAsync(string refreshToken)
    {
        await using var scope = fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IAuthSessionService>().RefreshAsync(refreshToken, "127.0.0.1");
    }

    private async Task<List<RefreshToken>> TokensOfAsync(Guid userId)
    {
        await using var scope = fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>()
            .RefreshTokens.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
    }

    [Fact]
    public async Task Issued_refresh_token_is_persisted_and_refreshable()
    {
        var user = await fixture.CreateUserAsync();
        var session = await LoginAsync(user);

        var refreshed = await RefreshAsync(session.RefreshToken);

        refreshed.RefreshToken.Should().NotBe(session.RefreshToken);
        var tokens = await TokensOfAsync(user.Id);
        tokens.Should().HaveCount(2);
        tokens.Select(t => t.FamilyId).Distinct().Should().ContainSingle();
        var old = tokens.Single(t => t.TokenHash == TokenHasher.Hash(session.RefreshToken));
        old.RevokedReason.Should().Be(RefreshToken.RotatedReason);
        old.ReplacedByTokenId.Should().Be(tokens.Single(t => t.Id != old.Id).Id);
    }

    [Fact]
    public async Task Two_parallel_refreshes_with_the_same_token_only_one_succeeds()
    {
        var user = await fixture.CreateUserAsync();
        var session = await LoginAsync(user);

        var attempts = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await RefreshAsync(session.RefreshToken);
                    return true;
                }
                catch (UnauthorizedException)
                {
                    return false;
                }
            }))
            .ToList();

        var results = await Task.WhenAll(attempts);

        results.Count(ok => ok).Should().Be(1);
        // The losers count as token reuse: the whole family (including the winner's new token) is revoked.
        (await TokensOfAsync(user.Id)).Should().OnlyContain(t => t.RevokedAt != null);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_family()
    {
        var user = await fixture.CreateUserAsync();
        var session = await LoginAsync(user);
        var rotated = await RefreshAsync(session.RefreshToken);

        await FluentActions.Invoking(() => RefreshAsync(session.RefreshToken))
            .Should().ThrowAsync<UnauthorizedException>();

        await FluentActions.Invoking(() => RefreshAsync(rotated.RefreshToken))
            .Should().ThrowAsync<UnauthorizedException>("the attacker's (or victim's) descendant token died with the family");
    }

    [Fact]
    public async Task Locked_user_cannot_refresh()
    {
        var user = await fixture.CreateUserAsync();
        var session = await LoginAsync(user);

        await using (var scope = fixture.NewScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var tracked = await users.FindByIdAsync(user.Id.ToString());
            await users.SetLockoutEnabledAsync(tracked!, true);
            await users.SetLockoutEndDateAsync(tracked!, DateTimeOffset.UtcNow.AddDays(1));
        }

        await FluentActions.Invoking(() => RefreshAsync(session.RefreshToken))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Soft_deleted_user_cannot_refresh_even_if_tokens_were_not_revoked()
    {
        var user = await fixture.CreateUserAsync();
        var session = await LoginAsync(user);

        await using (var scope = fixture.NewScope())
        {
            // Exactly what DeleteUser does to the row (minus the session revocation, to prove refresh checks state).
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            await db.Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.IsActive, false)
                    .SetProperty(u => u.LockoutEnd, DateTimeOffset.MaxValue));
        }

        await FluentActions.Invoking(() => RefreshAsync(session.RefreshToken))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Revoking_all_sessions_kills_every_refresh_token_and_changes_the_stamp()
    {
        var user = await fixture.CreateUserAsync();
        var first = await LoginAsync(user);
        var second = await LoginAsync(user);

        await using (var scope = fixture.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISessionRevoker>().RevokeAllAsync(user.Id, "test");
        }

        (await TokensOfAsync(user.Id)).Should().OnlyContain(t => t.RevokedAt != null);
        await FluentActions.Invoking(() => RefreshAsync(first.RefreshToken)).Should().ThrowAsync<UnauthorizedException>();
        await FluentActions.Invoking(() => RefreshAsync(second.RefreshToken)).Should().ThrowAsync<UnauthorizedException>();

        var fresh = await LoginAsync(user);
        await FluentActions.Invoking(() => RefreshAsync(fresh.RefreshToken)).Should().NotThrowAsync(
            "a new login after revocation carries the new security stamp");
    }

    [Fact]
    public async Task Cleanup_job_deletes_expired_tokens_in_batches_and_keeps_live_ones()
    {
        var user = await fixture.CreateUserAsync();
        var live = await LoginAsync(user);

        await using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            for (var i = 0; i < 2_500; i++)
            {
                db.RefreshTokens.Add(new RefreshToken
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    FamilyId = Guid.NewGuid(),
                    TokenHash = TokenHasher.Hash(Guid.NewGuid().ToString()),
                    SecurityStampHash = "x",
                    CreatedAt = DateTime.UtcNow.AddDays(-10),
                    ExpiresAt = DateTime.UtcNow.AddDays(-1),
                    CreatedByIp = "test"
                });
            }

            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.NewScope())
        {
            var job = new RefreshTokenCleanupJob(
                scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>(),
                scope.ServiceProvider.GetRequiredService<ISessionRevocationRecorder>(),
                NullLogger<RefreshTokenCleanupJob>.Instance);
            await job.ExecuteAsync();
        }

        var remaining = await TokensOfAsync(user.Id);
        remaining.Should().ContainSingle().Which.TokenHash.Should().Be(TokenHasher.Hash(live.RefreshToken));
    }
}
