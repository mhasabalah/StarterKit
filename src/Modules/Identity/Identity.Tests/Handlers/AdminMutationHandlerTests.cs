using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Services;
using Shared.Core.Exceptions;

namespace Identity.Tests.Handlers;

public sealed class AdminMutationHandlerTests
{
    private static readonly string[] PlainUser = [RoleConstants.User];
    private static readonly string[] AdminUser = [RoleConstants.User, RoleConstants.Admin];

    public static TheoryData<string> Mutations => new()
    {
        "Delete", "Edit", "Lock", "Unlock", "Deactivate", "Activate", "AssignRole", "RevokeRole",
        "Promote", "Demote", "ChangeAdminPassword", "UpdateAdmin", "CreateAdmin"
    };

    private static (AdminMutationFixture Fixture, Func<Task> Act) Build(string mutation)
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
    [MemberData(nameof(Mutations))]
    public async Task Every_mutation_is_blocked_by_the_role_hierarchy_guard_before_changing_anything(string mutation)
    {
        var (f, act) = Build(mutation);
        f.GuardForbidsEverything();

        await act.Should().ThrowAsync<ForbiddenException>();

        f.VerifyNothingChanged();
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Every_mutation_consults_the_guard(string mutation)
    {
        var (f, act) = Build(mutation);

        await act();

        f.Guard.Invocations.Should().NotBeEmpty($"{mutation} must call IRoleHierarchyGuard");
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("Lock")]
    [InlineData("Deactivate")]
    [InlineData("AssignRole")]
    [InlineData("RevokeRole")]
    [InlineData("Promote")]
    [InlineData("Demote")]
    [InlineData("ChangeAdminPassword")]
    public async Task Session_ending_mutations_revoke_all_sessions_of_the_target(string mutation)
    {
        var (f, act) = Build(mutation);

        await act();

        f.VerifyRevoked(Times.Once());
    }

    [Theory]
    [InlineData("Activate")]
    [InlineData("Unlock")]
    [InlineData("Edit")]
    [InlineData("UpdateAdmin")]
    public async Task Non_destructive_mutations_keep_sessions(string mutation)
    {
        var (f, act) = Build(mutation);

        await act();

        f.VerifyRevoked(Times.Never());
    }

    [Theory]
    [InlineData("Edit", true, false)]
    [InlineData("Edit", false, true)]
    [InlineData("UpdateAdmin", true, true)]
    public async Task Contact_change_by_admin_revokes_sessions(string mutation, bool emailChanged, bool phoneChanged)
    {
        var (f, act) = Build(mutation);
        f.Contact.Setup(c => c.ApplyAsync(It.IsAny<Domains.Users.Models.ApplicationUser>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContactChange(emailChanged, phoneChanged));

        await act();

        f.VerifyRevoked(Times.Once());
    }

    [Theory]
    [InlineData(RoleConstants.Admin)]
    [InlineData(RoleConstants.SuperAdmin)]
    [InlineData(RoleConstants.Finance)]
    [InlineData(RoleConstants.Support)]
    public async Task Granting_staff_roles_requires_a_superadmin(string role)
    {
        var f = new AdminMutationFixture(PlainUser);
        f.Users.Setup(m => m.IsInRoleAsync(f.Target, role)).ReturnsAsync(false);

        await f.AssignRole(role);

        f.Guard.Verify(g => g.EnsureIsSuperAdminAsync(f.Caller.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAdmin_requires_a_superadmin()
    {
        var f = new AdminMutationFixture(PlainUser);

        await f.CreateAdmin();

        f.Guard.Verify(g => g.EnsureIsSuperAdminAsync(f.Caller.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Granting_the_user_role_does_not_require_a_superadmin()
    {
        var f = new AdminMutationFixture(PlainUser);
        f.Users.Setup(m => m.IsInRoleAsync(f.Target, RoleConstants.User)).ReturnsAsync(false);

        await f.AssignRole(RoleConstants.User);

        f.Guard.Verify(g => g.EnsureIsSuperAdminAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
