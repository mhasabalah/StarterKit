using Identity.Data;
using Identity.Data.Repository.ApiKeys;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Users.Models;
using Identity.Models;
using Identity.Services.Sessions;
using Identity.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;

namespace Identity.IntegrationTests;

[Collection(IdentityPostgresCollection.Name)]
public sealed class SessionsAndApiKeysTests(IdentityPostgresFixture fixture)
{
    private async Task<AuthSession> LoginAsync(ApplicationUser user)
    {
        await using var scope = fixture.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tracked = await users.FindByIdAsync(user.Id.ToString());
        return await scope.ServiceProvider.GetRequiredService<IAuthSessionService>().IssueAsync(tracked!, "10.0.0.1");
    }

    private async Task<List<ActiveSession>> ActiveAsync(Guid userId)
    {
        await using var scope = fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>()
            .ListActiveFamiliesAsync(userId, DateTime.UtcNow);
    }

    private async Task<List<Shared.Core.Messaging.Outbox.OutboxMessage>> RevocationOutboxAsync(Guid userId)
    {
        await using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        var rows = await db.Set<Shared.Core.Messaging.Outbox.OutboxMessage>()
            .Where(m => m.MessageType.EndsWith("userdirectory.user-sessions-revoked.v1")).ToListAsync();
        return rows.Where(m => m.Payload.Contains(userId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    [Fact]
    public async Task Revoking_a_session_writes_a_sessions_revoked_event_to_the_outbox_with_that_sid()
    {
        var user = await fixture.CreateUserAsync();
        await LoginAsync(user);
        var sid = (await ActiveAsync(user.Id)).Single().FamilyId;

        await using (var scope = fixture.NewScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ISessionManager>().RevokeAsync(user.Id, sid, "test")).Should().BeTrue();
        }

        var rows = await RevocationOutboxAsync(user.Id);
        rows.Should().ContainSingle();
        rows[0].Payload.Should().Contain(sid.ToString()).And.Contain("\"allSessions\": false");
    }

    [Fact]
    public async Task Revoking_all_sessions_writes_an_all_sessions_event_to_the_outbox()
    {
        var user = await fixture.CreateUserAsync();
        await LoginAsync(user);

        await using (var scope = fixture.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISessionRevoker>().RevokeAllAsync(user.Id, "test");
        }

        var rows = await RevocationOutboxAsync(user.Id);
        rows.Should().ContainSingle().Which.Payload.Should().Contain("\"allSessions\": true");
    }

    [Fact]
    public async Task Login_creates_one_listed_session_per_device_with_device_metadata()
    {
        var user = await fixture.CreateUserAsync();
        await LoginAsync(user);
        await LoginAsync(user);

        var sessions = await ActiveAsync(user.Id);

        sessions.Should().HaveCount(2);
        sessions.Should().OnlyContain(s => s.UserAgent == "integration-tests" && s.IpAddress == "10.0.0.1");
        sessions.Select(s => s.FamilyId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Refreshing_keeps_a_single_listed_session_with_the_original_sign_in_time()
    {
        var user = await fixture.CreateUserAsync();
        var session = await LoginAsync(user);
        var before = (await ActiveAsync(user.Id)).Single();

        await using (var scope = fixture.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuthSessionService>().RefreshAsync(session.RefreshToken, "10.0.0.2");
        }

        var after = (await ActiveAsync(user.Id)).Single();
        after.FamilyId.Should().Be(before.FamilyId);
        after.SignedInAt.Should().BeCloseTo(before.SignedInAt, TimeSpan.FromMilliseconds(1));
        after.IpAddress.Should().Be("10.0.0.2");
    }

    [Fact]
    public async Task Revoking_a_session_ends_it_and_blacklists_the_sid_without_touching_other_sessions_or_users()
    {
        var user = await fixture.CreateUserAsync();
        var stranger = await fixture.CreateUserAsync();
        await LoginAsync(user);
        await LoginAsync(user);
        await LoginAsync(stranger);
        var target = (await ActiveAsync(user.Id)).First().FamilyId;

        await using (var scope = fixture.NewScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<ISessionManager>();
            (await manager.RevokeAsync(stranger.Id, target, "foreign")).Should().BeFalse("a foreign session id revokes nothing");
            (await manager.RevokeAsync(user.Id, target, "mine")).Should().BeTrue();
            (await manager.RevokeAsync(user.Id, target, "again")).Should().BeFalse();
        }

        (await ActiveAsync(user.Id)).Should().ContainSingle().Which.FamilyId.Should().NotBe(target);
        (await ActiveAsync(stranger.Id)).Should().HaveCount(1);
        (await fixture.Services.GetRequiredService<ITokenBlacklistService>().IsSessionRevokedAsync(target)).Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_others_spares_the_current_session_and_blacklists_the_rest()
    {
        var user = await fixture.CreateUserAsync();
        await LoginAsync(user);
        await LoginAsync(user);
        await LoginAsync(user);
        var all = await ActiveAsync(user.Id);
        var current = all.First().FamilyId;

        await using (var scope = fixture.NewScope())
        {
            var count = await scope.ServiceProvider.GetRequiredService<ISessionManager>()
                .RevokeOthersAsync(user.Id, current, "others");
            count.Should().Be(2);
        }

        (await ActiveAsync(user.Id)).Should().ContainSingle().Which.FamilyId.Should().Be(current);
        var blacklist = fixture.Services.GetRequiredService<ITokenBlacklistService>();
        foreach (var other in all.Skip(1))
            (await blacklist.IsSessionRevokedAsync(other.FamilyId)).Should().BeTrue();
        (await blacklist.IsSessionRevokedAsync(current)).Should().BeFalse();
    }

    [Fact]
    public async Task Api_key_round_trips_scopes_is_found_by_prefix_and_dies_with_its_owner()
    {
        var user = await fixture.CreateUserAsync();
        var (key, raw) = ApiKey.Create(user.Id, Guid.NewGuid(), "ci", [Permissions.BillingRead, "catalog.products.manage"],
            DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        await using (var scope = fixture.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IApiKeyRepository>().AddAsync(key);
        }

        await using (var scope = fixture.NewScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
            var found = await repo.FindByPrefixAsync(key.Prefix);
            found!.Scopes.Should().BeEquivalentTo(key.Scopes);
            found.Matches(raw).Should().BeTrue();
            found.TenantId.Should().Be(key.TenantId);
            (await repo.FindOwnedAsync(Guid.NewGuid(), key.Id)).Should().BeNull("scoped to the owner");
            (await repo.CountActiveAsync(user.Id, DateTime.UtcNow)).Should().Be(1);

            await repo.TouchAsync(key.Id, DateTime.UtcNow);
            var owned = await repo.FindOwnedAsync(user.Id, key.Id);
            owned!.LastUsedAt.Should().NotBeNull();
            owned.Revoke(DateTime.UtcNow, user.Id);
            await repo.SaveAsync();
            (await repo.CountActiveAsync(user.Id, DateTime.UtcNow)).Should().Be(0);
        }

        await using (var scope = fixture.NewScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.DeleteAsync((await users.FindByIdAsync(user.Id.ToString()))!);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            (await db.ApiKeys.AnyAsync(k => k.Id == key.Id)).Should().BeFalse("keys cascade with the user");
        }
    }

    [Fact]
    public async Task Two_keys_cannot_share_a_prefix()
    {
        var user = await fixture.CreateUserAsync();
        var a = ApiKey.Create(user.Id, null, "a", [], DateTime.UtcNow, DateTime.UtcNow.AddDays(1)).Key;
        await using (var scope = fixture.NewScope())
        {
            await scope.ServiceProvider.GetRequiredService<IApiKeyRepository>().AddAsync(a);
        }

        var clone = ApiKey.Create(user.Id, null, "b", [], DateTime.UtcNow, DateTime.UtcNow.AddDays(1)).Key;
        typeof(ApiKey).GetProperty(nameof(ApiKey.Prefix))!.SetValue(clone, a.Prefix);
        await using var scope2 = fixture.NewScope();

        await scope2.ServiceProvider.GetRequiredService<IApiKeyRepository>().Invoking(r => r.AddAsync(clone))
            .Should().ThrowAsync<DbUpdateException>();
    }
}
