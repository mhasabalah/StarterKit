using Identity.Contracts.Admins.Commands;
using Identity.Contracts.Users.Commands;
using Identity.Domains.Admins.Features.AssignRole;
using Identity.Domains.Admins.Features.ChangeAdminPassword;
using Identity.Domains.Admins.Features.CreateAdmin;
using Identity.Domains.Admins.Features.DemoteAdmin;
using Identity.Domains.Admins.Features.PromoteToAdmin;
using Identity.Domains.Admins.Features.RevokeRole;
using Identity.Domains.Admins.Features.UpdateAdmin;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Roles.Models;
using Identity.Domains.Users.Features.DeleteUser;
using Identity.Domains.Users.Features.EditUser;
using Identity.Domains.Users.Features.LockUser;
using Identity.Domains.Users.Features.UnlockUser;
using Identity.Domains.Users.Features.UpdateUserStatus;
using Identity.Domains.Users.Models;
using Identity.Domains.Users.Services;
using Identity.Services.Phone;
using Identity.Services.Sessions;
using Shared.Contracts.Mediation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.Web;

namespace Identity.Tests.Handlers;

/// <summary>Builds every user/admin mutation handler on shared mocks.</summary>
public sealed class AdminMutationFixture
{
    public Mock<UserManager<ApplicationUser>> Users { get; } = Mocks.UserManager();
    public Mock<RoleManager<ApplicationRole>> RoleManager { get; }
    public Mock<IRoleHierarchyGuard> Guard { get; } = new();
    public Mock<ISessionRevoker> Revoker { get; } = new();
    public Mock<IPublisher> Publisher { get; } = new();
    public Mock<IHttpContextService> Http { get; } = new();
    public Mock<IUserContactService> Contact { get; } = new();
    public Mock<IPhoneNumberNormalizer> Phone { get; } = new();
    public RecordingAuditWriter Audit { get; } = new();

    public ApplicationUser Caller { get; } = Mocks.User();
    public ApplicationUser Target { get; } = Mocks.User();

    public AdminMutationFixture(string[] targetRoles)
    {
        RoleManager = new Mock<RoleManager<ApplicationRole>>(
            Mock.Of<IRoleStore<ApplicationRole>>(), null!, null!, null!, null!);
        RoleManager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        Users.WithUsers((Caller, [RoleConstants.User, RoleConstants.Admin, RoleConstants.SuperAdmin]), (Target, targetRoles));
        var ok = IdentityResult.Success;
        Users.Setup(m => m.SetLockoutEnabledAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>())).ReturnsAsync(ok);
        Users.Setup(m => m.SetLockoutEndDateAsync(It.IsAny<ApplicationUser>(), It.IsAny<DateTimeOffset?>())).ReturnsAsync(ok);
        Users.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(ok);
        Users.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(ok);
        Users.Setup(m => m.RemoveFromRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(ok);
        Users.Setup(m => m.IsInRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser _, string role) => role == RoleConstants.Admin);
        Users.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>())).ReturnsAsync("token");
        Users.Setup(m => m.ResetPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(ok);
        Users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(ok);

        Http.Setup(h => h.GetUserIdAsGuid()).Returns(Caller.Id);
        Contact.Setup(c => c.ApplyAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>())).ReturnsAsync(new ContactChange(false, false));
    }

    public void GuardForbidsEverything()
    {
        Guard.Setup(g => g.EnsureCanManageAsync(It.IsAny<Guid>(), It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Shared.Core.Exceptions.ForbiddenException("nope"));
        Guard.Setup(g => g.EnsureIsSuperAdminAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Shared.Core.Exceptions.ForbiddenException("nope"));
    }

    public Task Delete() => new DeleteUserHandler(Users.Object, Guard.Object, Revoker.Object, Publisher.Object,
            Audit, NullLogger<DeleteUserHandler>.Instance)
        .Handle(new DeleteUserCommand { UserId = Target.Id, Reason = "reason long enough", DeletedByAdminId = Caller.Id }, default);

    public Task Edit() => new EditUserHandler(Users.Object, Http.Object, Guard.Object, Contact.Object, Revoker.Object,
            Audit, NullLogger<EditUserHandler>.Instance)
        .Handle(new EditUserCommand { UserId = Target.Id, Email = "new@example.com" }, default);

    public Task Lock() => new LockUserHandler(Users.Object, Guard.Object, Revoker.Object, Publisher.Object,
            Audit, NullLogger<LockUserHandler>.Instance)
        .Handle(new LockUserCommand { UserId = Target.Id, Reason = "reason long enough", LockedByAdminId = Caller.Id }, default);

    public Task Unlock()
    {
        Users.Setup(m => m.IsLockedOutAsync(Target)).ReturnsAsync(true);
        return new UnlockUserHandler(Users.Object, Guard.Object, Publisher.Object, Audit, NullLogger<UnlockUserHandler>.Instance)
            .Handle(new UnlockUserCommand { UserId = Target.Id, UnlockedByAdminId = Caller.Id }, default);
    }

    public Task SetStatus(bool active)
    {
        Target.IsActive = !active;
        return new UpdateUserStatusHandler(Users.Object, Guard.Object, Revoker.Object, Publisher.Object,
                Audit, NullLogger<UpdateUserStatusHandler>.Instance)
            .Handle(new UpdateUserStatusCommand { UserId = Target.Id, IsActive = active, UpdatedByAdminId = Caller.Id }, default);
    }

    public Task AssignRole(string role = RoleConstants.User) => new AssignRoleHandler(Users.Object, RoleManager.Object,
            Guard.Object, Revoker.Object, Publisher.Object, Audit, NullLogger<AssignRoleHandler>.Instance)
        .Handle(new AssignRoleCommand { UserId = Target.Id, RoleName = role, AssignedByAdminId = Caller.Id }, default);

    public Task RevokeRole() => new RevokeRoleHandler(Users.Object, Guard.Object, Revoker.Object, Publisher.Object,
            Audit, NullLogger<RevokeRoleHandler>.Instance)
        .Handle(new RevokeRoleCommand { UserId = Target.Id, RoleName = RoleConstants.Admin, RevokedByAdminId = Caller.Id }, default);

    public Task Promote() => new PromoteToAdminHandler(Users.Object, Guard.Object, Revoker.Object, Publisher.Object,
            Audit, NullLogger<PromoteToAdminHandler>.Instance)
        .Handle(new PromoteToAdminCommand { UserId = Target.Id, Reason = "reason long enough", PromotedByAdminId = Caller.Id }, default);

    public Task Demote() => new DemoteAdminHandler(Users.Object, Guard.Object, Revoker.Object, Publisher.Object,
            Audit, NullLogger<DemoteAdminHandler>.Instance)
        .Handle(new DemoteAdminCommand { AdminId = Target.Id, Reason = "reason long enough", DemotedByAdminId = Caller.Id }, default);

    public Task ChangeAdminPassword() => new ChangeAdminPasswordHandler(Users.Object, Guard.Object, Revoker.Object,
            Audit, NullLogger<ChangeAdminPasswordHandler>.Instance)
        .Handle(new AdminChangePasswordCommand { AdminId = Target.Id, NewPassword = "N3w!Passw0rd", ChangedByAdminId = Caller.Id }, default);

    public Task UpdateAdmin() => new UpdateAdminHandler(Users.Object, Http.Object, Guard.Object, Contact.Object,
            Revoker.Object, Audit, NullLogger<UpdateAdminHandler>.Instance)
        .Handle(new UpdateAdminCommand { AdminId = Target.Id, Email = "new@example.com" }, default);

    public Task CreateAdmin() => new CreateAdminHandler(Users.Object, Guard.Object, Phone.Object, Publisher.Object,
            Audit, NullLogger<CreateAdminHandler>.Instance)
        .Handle(new CreateAdminCommand
        {
            UserName = "newadmin", Email = "admin2@example.com", Password = "N3w!Passw0rd", CreatedByAdminId = Caller.Id
        }, default);

    public void VerifyNothingChanged()
    {
        Users.Verify(m => m.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        Users.Verify(m => m.SetLockoutEndDateAsync(It.IsAny<ApplicationUser>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        Users.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        Users.Verify(m => m.RemoveFromRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        Users.Verify(m => m.ResetPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Users.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        Contact.Verify(c => c.ApplyAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Revoker.Verify(r => r.RevokeAllAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Publisher.Invocations.Should().BeEmpty();
        Audit.Entries.Should().BeEmpty("a rejected mutation must not be audited as done");
    }

    public void VerifyRevoked(Times times) =>
        Revoker.Verify(r => r.RevokeAllAsync(Target.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), times);
}
