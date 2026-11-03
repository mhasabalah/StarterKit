using Identity.Contracts.Audit;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Domains.Roles.Constants;
using Identity.Tests.TwoFactor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Tests.Handlers;

public sealed class AdminAuditTests
{
    private static readonly string[] PlainUser = [RoleConstants.User];
    private static readonly string[] AdminUser = [RoleConstants.User, RoleConstants.Admin];

    public static TheoryData<string, string> MutationActions => new()
    {
        { "Delete", IdentityAuditActions.UserDeleted },
        { "Edit", IdentityAuditActions.UserEdited },
        { "Lock", IdentityAuditActions.UserLocked },
        { "Unlock", IdentityAuditActions.UserUnlocked },
        { "Deactivate", IdentityAuditActions.UserStatusChanged },
        { "Activate", IdentityAuditActions.UserStatusChanged },
        { "AssignRole", IdentityAuditActions.RoleAssigned },
        { "RevokeRole", IdentityAuditActions.RoleRevoked },
        { "Promote", IdentityAuditActions.AdminPromoted },
        { "Demote", IdentityAuditActions.AdminDemoted },
        { "ChangeAdminPassword", IdentityAuditActions.AdminPasswordChanged },
        { "UpdateAdmin", IdentityAuditActions.AdminUpdated },
        { "CreateAdmin", IdentityAuditActions.AdminCreated }
    };

    private static (AdminMutationFixture F, Func<Task> Act) Build(string mutation)
    {
        var adminTarget = mutation is "Demote" or "ChangeAdminPassword" or "UpdateAdmin" or "RevokeRole";
        var f = new AdminMutationFixture(adminTarget ? AdminUser : PlainUser);
        Func<Task> act = mutation switch
        {
            "Delete" => f.Delete,
            "Edit" => f.Edit,
            "Lock" => f.Lock,
            "Unlock" => f.Unlock,
            "Deactivate" => () => f.SetStatus(false),
            "Activate" => () => f.SetStatus(true),
            "AssignRole" => () => f.AssignRole(),
            "RevokeRole" => f.RevokeRole,
            "Promote" => f.Promote,
            "Demote" => f.Demote,
            "ChangeAdminPassword" => f.ChangeAdminPassword,
            "UpdateAdmin" => f.UpdateAdmin,
            "CreateAdmin" => f.CreateAdmin,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        return (f, act);
    }

    [Theory]
    [MemberData(nameof(MutationActions))]
    public async Task Every_admin_mutation_writes_one_audit_entry_by_the_acting_admin(string mutation, string action)
    {
        var (f, act) = Build(mutation);

        await act();

        var entry = f.Audit.Entries.Should().ContainSingle().Which;
        entry.Action.Should().Be(action);
        entry.ActorId.Should().Be(f.Caller.Id);
        entry.TargetType.Should().Be(IdentityAuditTargets.User);
        if (mutation != "CreateAdmin")
        {
            entry.TargetId.Should().Be(f.Target.Id);
        }
    }

    [Theory]
    [MemberData(nameof(MutationActions))]
    public async Task Audit_entries_never_contain_secrets(string mutation, string _)
    {
        var (f, act) = Build(mutation);
        f.Target.PasswordHash = "AQAAAAIAAYagAAAAE-password-hash";
        f.Target.ConcurrencyStamp = "concurrency-stamp-value";

        await act();

        var json = string.Concat(f.Audit.Entries.Select(e => e.BeforeJson + e.AfterJson));
        json.Should().NotContain("N3w!Passw0rd", "passwords are never audited")
            .And.NotContain(f.Target.SecurityStamp!)
            .And.NotContain(f.Target.PasswordHash)
            .And.NotContainEquivalentOf("passwordHash")
            .And.NotContainEquivalentOf("securityStamp")
            .And.NotContainEquivalentOf("token");
    }

    [Fact]
    public void The_audit_snapshot_has_no_credential_fields()
    {
        var forbidden = new[] { "password", "hash", "stamp", "token", "secret", "key", "code" };

        typeof(UserAuditSnapshot).GetProperties().Select(p => p.Name.ToLowerInvariant())
            .Should().NotContain(name => forbidden.Any(name.Contains));
    }

    [Fact]
    public void Every_user_and_admin_mutation_command_runs_in_an_audited_unit_of_work()
    {
        var mutations = typeof(IAuditedCommand).Assembly.GetTypes()
            .Where(t => t.Namespace is "Identity.Contracts.Admins.Commands" or "Identity.Contracts.Users.Commands")
            .Where(t => t.Name.EndsWith("Command"))
            .ToList();

        mutations.Should().HaveCountGreaterThanOrEqualTo(13);
        mutations.Should().OnlyContain(t => typeof(IAuditedCommand).IsAssignableFrom(t));
    }

    [Fact]
    public async Task Audit_rows_are_append_only()
    {
        await using var host = new TwoFactorTestHost();
        var writer = host.Scope.GetRequiredService<IIdentityAuditWriter>();
        writer.Record(IdentityAuditActions.UserLocked, Guid.NewGuid(), reason: "first", actorId: Guid.NewGuid());
        await host.Db.SaveChangesAsync();

        var row = await host.Db.AdminAuditLogs.SingleAsync();
        host.Db.Entry(row).Property(r => r.Reason).CurrentValue = "rewritten";
        await host.Db.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>();

        host.Db.ChangeTracker.Clear();
        host.Db.AdminAuditLogs.Remove(await host.Db.AdminAuditLogs.SingleAsync());
        await host.Db.Invoking(db => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Writer_captures_request_context()
    {
        await using var host = new TwoFactorTestHost();
        host.Http.Setup(h => h.GetUserAgent()).Returns("test-agent");
        host.Http.Setup(h => h.GetCorrelationId()).Returns("corr-1");
        var admin = Guid.NewGuid();
        host.Http.Setup(h => h.GetUserIdAsGuid()).Returns(admin);

        host.Scope.GetRequiredService<IIdentityAuditWriter>()
            .Record(IdentityAuditActions.UserEdited, Guid.NewGuid(), before: new { A = 1 }, after: new { A = 2 });
        await host.Db.SaveChangesAsync();

        var row = await host.Db.AdminAuditLogs.AsNoTracking().SingleAsync();
        row.AdminId.Should().Be(admin);
        row.IpAddress.Should().Be("127.0.0.1");
        row.UserAgent.Should().Be("test-agent");
        row.CorrelationId.Should().Be("corr-1");
        row.OccurredAt.Should().Be(host.Clock.GetUtcNow());
        row.BeforeJson.Should().Be("{\"a\":1}");
        row.AfterJson.Should().Be("{\"a\":2}");
    }

    [Fact]
    public async Task A_failed_unit_of_work_does_not_leave_its_audit_row_behind()
    {
        await using var host = new TwoFactorTestHost();
        var unitOfWork = host.Scope.GetRequiredService<IIdentityUnitOfWork>();
        var writer = host.Scope.GetRequiredService<IIdentityAuditWriter>();

        await unitOfWork.Invoking(u => u.ExecuteAsync<int>(() =>
        {
            writer.Record(IdentityAuditActions.UserLocked, Guid.NewGuid(), actorId: Guid.NewGuid());
            throw new InvalidOperationException("boom");
        })).Should().ThrowAsync<InvalidOperationException>();

        // A later save in the same scope must not persist the half-done unit of work.
        await host.Db.SaveChangesAsync();
        (await host.Db.AdminAuditLogs.CountAsync()).Should().Be(0);

        await unitOfWork.ExecuteAsync(() =>
        {
            writer.Record(IdentityAuditActions.UserLocked, Guid.NewGuid(), actorId: Guid.NewGuid());
            return Task.FromResult(0);
        });
        (await host.Db.AdminAuditLogs.CountAsync()).Should().Be(1, "a successful unit of work saves its audit rows");
    }
}
