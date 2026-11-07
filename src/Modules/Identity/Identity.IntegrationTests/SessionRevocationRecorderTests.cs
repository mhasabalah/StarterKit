using Identity.Data;
using Identity.Domains.Sessions.Models;
using Identity.Domains.Users.Models;
using Identity.Infrastructure.BackgroundJobs;
using Identity.Data.Repository.RefreshTokens;
using Identity.Services.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Data;
using Shared.Core.Messaging.Outbox;

namespace Identity.IntegrationTests;

/// <summary>
/// The revocation announcement is atomic with the caller's transaction when there is one, and never takes the caller's
/// other pending changes with it when there is none.
/// </summary>
[Collection(IdentityPostgresCollection.Name)]
public sealed class SessionRevocationRecorderTests(IdentityPostgresFixture fixture)
{
    private async Task<(int Tombstones, int Outbox)> CountsAsync(Guid userId)
    {
        await using var scope = fixture.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        var tombstones = await db.SessionRevocations.CountAsync(r => r.UserId == userId);
        var outbox = (await db.Set<OutboxMessage>().Where(m => m.MessageType.EndsWith("userdirectory.user-sessions-revoked.v1")).ToListAsync())
            .Count(m => m.Payload.Contains(userId.ToString(), StringComparison.OrdinalIgnoreCase));
        return (tombstones, outbox);
    }

    [Fact]
    public async Task Inside_an_open_transaction_the_announcement_commits_with_it_and_rolls_back_with_it()
    {
        Guid committedUser = Guid.NewGuid(), rolledBackUser = Guid.NewGuid();

        await using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var recorder = scope.ServiceProvider.GetRequiredService<ISessionRevocationRecorder>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await recorder.RecordAllSessionsAsync(rolledBackUser);
            await db.SaveChangesAsync(); // the unit of work's own save
            await tx.RollbackAsync();
        }

        await using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var recorder = scope.ServiceProvider.GetRequiredService<ISessionRevocationRecorder>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await recorder.RecordAllSessionsAsync(committedUser);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        (await CountsAsync(rolledBackUser)).Should().Be((0, 0));
        (await CountsAsync(committedUser)).Should().Be((1, 1));
    }

    [Fact]
    public async Task Inside_a_transaction_the_recorder_itself_saves_nothing()
    {
        var user = Guid.NewGuid();

        await using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<ISessionRevocationRecorder>().RecordAllSessionsAsync(user);
            (await CountsAsync(user)).Should().Be((0, 0), "the caller's unit of work owns the save");
            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task Without_a_transaction_only_the_tombstone_is_saved_and_other_pending_changes_stay_pending()
    {
        var user = Guid.NewGuid();
        var other = await fixture.CreateUserAsync();

        await using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var tracked = await db.Users.SingleAsync(u => u.Id == other.Id);
            tracked.FirstName = "Pending-change";

            await scope.ServiceProvider.GetRequiredService<ISessionRevocationRecorder>().RecordSessionsAsync(user, [Guid.NewGuid()]);

            db.Entry(tracked).State.Should().Be(EntityState.Modified, "the recorder must not flush the caller's tracked entities");
        }

        (await CountsAsync(user)).Should().Be((1, 1));
        await using var verify = fixture.NewScope();
        (await verify.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Users.AsNoTracking()
            .SingleAsync(u => u.Id == other.Id)).FirstName.Should().NotBe("Pending-change");
    }

    [Fact]
    public async Task A_failing_save_is_swallowed_and_leaves_no_rows()
    {
        var user = Guid.NewGuid();
        var runner = new Mock<IUnitOfWorkRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("db down"));

        await using var scope = fixture.NewScope();
        var recorder = new SessionRevocationRecorder(scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>(),
            runner.Object, TimeProvider.System, NullLogger<SessionRevocationRecorder>.Instance);

        await recorder.Invoking(r => r.RecordAllSessionsAsync(user)).Should().NotThrowAsync();
        (await CountsAsync(user)).Should().Be((0, 0));
    }

    [Fact]
    public async Task The_cleanup_job_purges_old_revocation_rows_and_keeps_recent_ones()
    {
        Guid oldUser = Guid.NewGuid(), recentUser = Guid.NewGuid();
        await using (var scope = fixture.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            db.SessionRevocations.Add(SessionRevocation.Create(oldUser, null, true, DateTime.UtcNow.AddDays(-30)));
            db.SessionRevocations.Add(SessionRevocation.Create(recentUser, null, true, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.NewScope())
        {
            var job = new RefreshTokenCleanupJob(
                scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>(),
                scope.ServiceProvider.GetRequiredService<ISessionRevocationRecorder>(),
                TimeProvider.System, NullLogger<RefreshTokenCleanupJob>.Instance);
            await job.ExecuteAsync(CancellationToken.None);
        }

        (await CountsAsync(oldUser)).Tombstones.Should().Be(0);
        (await CountsAsync(recentUser)).Tombstones.Should().Be(1);
    }
}
