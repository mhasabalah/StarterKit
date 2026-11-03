using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Exceptions;

namespace Identity.Tests.Authorization;

public sealed class RoleHierarchyTests
{
    private static readonly string[] User = [RoleConstants.User];
    private static readonly string[] Admin = [RoleConstants.User, RoleConstants.Admin];
    private static readonly string[] SuperAdmin = [RoleConstants.User, RoleConstants.Admin, RoleConstants.SuperAdmin];
    private static readonly string[] OnlySuperAdmin = [RoleConstants.SuperAdmin];
    private static readonly string[] Finance = [RoleConstants.User, RoleConstants.Finance];
    private static readonly string[] Support = [RoleConstants.User, RoleConstants.Support];

    public static TheoryData<string, string, bool> Matrix => new()
    {
        // caller,      target,       allowed
        { "SuperAdmin", "User", true },
        { "SuperAdmin", "Admin", true },
        { "SuperAdmin", "SuperAdmin", true },
        { "Admin", "User", true },
        { "Admin", "Admin", false },
        { "Admin", "SuperAdmin", false },
        { "Admin", "OnlySuperAdmin", false },
        { "User", "User", false },
        { "User", "Admin", false },
        { "User", "SuperAdmin", false },
        { "None", "User", false },
        // Staff roles: only a SuperAdmin manages them; they manage nobody.
        { "SuperAdmin", "Finance", true },
        { "SuperAdmin", "Support", true },
        { "Admin", "Finance", false },
        { "Admin", "Support", false },
        { "Finance", "User", false },
        { "Support", "User", false }
    };

    private static string[] Roles(string name) => name switch
    {
        "User" => User,
        "Admin" => Admin,
        "SuperAdmin" => SuperAdmin,
        "OnlySuperAdmin" => OnlySuperAdmin,
        "Finance" => Finance,
        "Support" => Support,
        _ => []
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void CanManage_matrix(string caller, string target, bool allowed)
    {
        RoleHierarchy.CanManage(Guid.NewGuid(), Roles(caller), Guid.NewGuid(), Roles(target))
            .Should().Be(allowed);
    }

    [Theory]
    [InlineData("SuperAdmin")]
    [InlineData("Admin")]
    [InlineData("User")]
    public void Nobody_manages_themselves(string role)
    {
        var id = Guid.NewGuid();
        RoleHierarchy.CanManage(id, Roles(role), id, Roles(role)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Guard_uses_roles_from_the_store(string caller, string target, bool allowed)
    {
        var userManager = Mocks.UserManager();
        var callerUser = Mocks.User();
        var targetUser = Mocks.User();
        userManager.WithUsers((callerUser, Roles(caller)), (targetUser, Roles(target)));

        var guard = new RoleHierarchyGuard(userManager.Object, NullLogger<RoleHierarchyGuard>.Instance);
        var act = () => guard.EnsureCanManageAsync(callerUser.Id, targetUser);

        if (allowed)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            await act.Should().ThrowAsync<ForbiddenException>();
        }
    }

    [Fact]
    public async Task Guard_rejects_unknown_or_deactivated_callers()
    {
        var userManager = Mocks.UserManager();
        var inactiveSuperAdmin = Mocks.User(active: false);
        var target = Mocks.User();
        userManager.WithUsers((inactiveSuperAdmin, SuperAdmin), (target, User));
        var guard = new RoleHierarchyGuard(userManager.Object, NullLogger<RoleHierarchyGuard>.Instance);

        await guard.Invoking(g => g.EnsureCanManageAsync(inactiveSuperAdmin.Id, target))
            .Should().ThrowAsync<ForbiddenException>();
        await guard.Invoking(g => g.EnsureCanManageAsync(Guid.NewGuid(), target))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData("SuperAdmin", true)]
    [InlineData("Admin", false)]
    [InlineData("User", false)]
    public async Task EnsureIsSuperAdmin(string caller, bool allowed)
    {
        var userManager = Mocks.UserManager();
        var callerUser = Mocks.User();
        userManager.WithUsers((callerUser, Roles(caller)));
        var guard = new RoleHierarchyGuard(userManager.Object, NullLogger<RoleHierarchyGuard>.Instance);

        var act = () => guard.EnsureIsSuperAdminAsync(callerUser.Id);

        if (allowed)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            await act.Should().ThrowAsync<ForbiddenException>();
        }
    }

    [Fact]
    public void Identity_and_shared_role_constants_stay_in_sync()
    {
        RoleConstants.GetAllRoles().Should().BeEquivalentTo(SharedRoleNames());
    }

    private static string[] SharedRoleNames() => typeof(Shared.Core.Authorization.RoleConstants)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}
