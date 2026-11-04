using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Users.Commands;
using Identity.Contracts.Users.Queries;
using Identity.Data;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Domains.Users.Features.DeleteMyAccount;
using Identity.Domains.Users.Models;
using Identity.Services.Sessions;
using Shared.Contracts.Mediation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Exceptions;
using Tenancy.Contracts.Tenants;
using UserDirectory.Contracts.Users.IntegrationEvents;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity.IntegrationTests;

/// <summary>Self-service account deletion (erasure) and personal-data export against PostgreSQL.</summary>
[Collection(IdentityPostgresCollection.Name)]
public sealed class AccountLifecycleTests(IdentityPostgresFixture fixture)
{
    private async Task<T> SendAsync<T>(IRequest<T> request)
    {
        await using var scope = fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private async Task<TResult> WithDbAsync<TResult>(Func<ApplicationIdentityDbContext, Task<TResult>> query)
    {
        await using var scope = fixture.NewScope();
        return await query(scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>());
    }

    private async Task<ApplicationUser> ReloadAsync(Guid id)
    {
        await using var scope = fixture.NewScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(id.ToString()))!;
    }

    private async Task SignInAsync(ApplicationUser user)
    {
        await using var scope = fixture.NewScope();
        await scope.ServiceProvider.GetRequiredService<IAuthSessionService>().IssueAsync(user, "10.0.0.1");
    }

    private async Task AddGoogleLoginAsync(ApplicationUser user)
    {
        await using var scope = fixture.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tracked = (await users.FindByIdAsync(user.Id.ToString()))!;
        (await users.AddLoginAsync(tracked, new UserLoginInfo("Google", Guid.NewGuid().ToString(), "Google"))).Succeeded
            .Should().BeTrue();
    }

    private async Task<ApplicationUser> CreatePasswordlessUserAsync(string phone)
    {
        await using var scope = fixture.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = $"phone{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.com",
            PhoneNumber = phone, IsPhoneNumberVerified = true, IsActive = true
        };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, RoleConstants.User);
        return user;
    }

    // ----- deletion -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_WithPassword_AnonymizesTheUser_EndsSessions_AndRecordsATombstoneAndAnAuditEntryWithoutPii()
    {
        var user = await fixture.CreateUserAsync("+201001112233");
        await AddGoogleLoginAsync(user);
        await SignInAsync(user);
        var email = user.Email!;
        var stampBefore = (await ReloadAsync(user.Id)).SecurityStamp;

        await SendAsync(new DeleteMyAccountCommand(user.Id, IdentityPostgresFixture.Password));

        var after = await ReloadAsync(user.Id);
        after.Should().NotBeNull("the row is kept for referential integrity");
        after.Email.Should().NotBe(email).And.EndWith("@deleted.invalid");
        after.UserName.Should().StartWith("deleted-");
        after.PhoneNumber.Should().BeNull();
        after.FirstName.Should().Be("Deleted");
        after.ProfilePictureUrl.Should().BeNull();
        after.PasswordHash.Should().BeNull();
        after.IsActive.Should().BeFalse();
        after.LockoutEnd.Should().Be(DateTimeOffset.MaxValue);
        after.SecurityStamp.Should().NotBe(stampBefore);

        await WithDbAsync(async db =>
        {
            (await db.UserLogins.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
            (await db.UserRoles.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
            (await db.RefreshTokens.CountAsync(x => x.UserId == user.Id)).Should().Be(0);
            (await db.AccountDeletions.CountAsync(x => x.Id == user.Id)).Should().Be(1);

            var audit = await db.AdminAuditLogs.SingleAsync(
                x => x.TargetId == user.Id && x.Action == IdentityAuditActions.AccountErased);
            audit.AdminId.Should().Be(user.Id);
            audit.BeforeJson.Should().BeNull();
            audit.AfterJson.Should().BeNull();
            audit.Reason.Should().BeNull();
            audit.IpAddress.Should().BeNull();
            audit.UserAgent.Should().BeNull();
            return 0;
        });

        var login = () => SendAsync(new LoginCommand(email, IdentityPostgresFixture.Password));
        await login.Should().ThrowAsync<Exception>("the erased account can never sign in");
    }

    [Fact]
    public async Task Delete_WhenALateStepFails_RollsEverythingBack_AndTheAccountStaysIntact()
    {
        var user = await fixture.CreateUserAsync("+201001112299");
        await AddGoogleLoginAsync(user);
        await SignInAsync(user);
        var stampBefore = (await ReloadAsync(user.Id)).SecurityStamp;

        await using (var scope = fixture.NewScope())
        {
            var sp = scope.ServiceProvider;
            // The audit write happens after the bulk deletes and the session revocation: make it blow up.
            var failingAudit = new Mock<IIdentityAuditWriter>();
            failingAudit.Setup(a => a.RecordAccountErasure(It.IsAny<Guid>())).Throws(new InvalidOperationException("boom"));
            var handler = new DeleteMyAccountHandler(
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                sp.GetRequiredService<ApplicationIdentityDbContext>(),
                sp.GetRequiredService<Identity.Services.Otp.IOtpCodeService>(),
                sp.GetRequiredService<ISessionRevoker>(),
                sp.GetRequiredService<ISender>(),
                failingAudit.Object,
                sp.GetRequiredService<Identity.Domains.Audit.Services.IIdentityUnitOfWork>(),
                TimeProvider.System,
                sp.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Identity.Resources.IdentityResource>>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DeleteMyAccountHandler>.Instance);

            var act = () => handler.Handle(new DeleteMyAccountCommand(user.Id, IdentityPostgresFixture.Password), CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        }

        var after = await ReloadAsync(user.Id);
        after.IsActive.Should().BeTrue();
        after.Email.Should().Be(user.Email);
        after.PasswordHash.Should().NotBeNull();
        after.SecurityStamp.Should().Be(stampBefore, "the stamp rotation was rolled back with everything else");
        await WithDbAsync(async db =>
        {
            (await db.UserLogins.CountAsync(x => x.UserId == user.Id)).Should().Be(1);
            (await db.UserRoles.CountAsync(x => x.UserId == user.Id)).Should().BeGreaterThan(0);
            (await db.RefreshTokens.CountAsync(x => x.UserId == user.Id && x.RevokedAt == null)).Should().BeGreaterThan(0);
            (await db.AccountDeletions.CountAsync(x => x.Id == user.Id)).Should().Be(0);
            return 0;
        });
    }

    [Fact]
    public async Task Delete_WithWrongPassword_IsRejected_AndNothingChanges()
    {
        var user = await fixture.CreateUserAsync();

        var act = () => SendAsync(new DeleteMyAccountCommand(user.Id, "Wrong-Passw0rd!"));

        await act.Should().ThrowAsync<BadRequestException>();
        var after = await ReloadAsync(user.Id);
        after.IsActive.Should().BeTrue();
        after.Email.Should().Be(user.Email);
        (await WithDbAsync(db => db.AccountDeletions.CountAsync(x => x.Id == user.Id))).Should().Be(0);
    }

    [Fact]
    public async Task Delete_WithoutAnyCredential_IsRejectedByTheValidator()
    {
        var user = await fixture.CreateUserAsync();

        var act = () => SendAsync(new DeleteMyAccountCommand(user.Id));

        await act.Should().ThrowAsync<Exception>();
        (await ReloadAsync(user.Id)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_WhenTheUserOwnsATenant_IsRefusedWithConflict_AndNothingChanges()
    {
        var user = await fixture.CreateUserAsync();
        fixture.OwnedTenants[user.Id] = [new OwnedTenantDto(Guid.NewGuid(), "acme", "Acme")];

        var act = () => SendAsync(new DeleteMyAccountCommand(user.Id, IdentityPostgresFixture.Password));

        (await act.Should().ThrowAsync<ConflictException>()).Which.Message.Should().Contain("acme");
        (await ReloadAsync(user.Id)).IsActive.Should().BeTrue();
        (await WithDbAsync(db => db.AccountDeletions.CountAsync(x => x.Id == user.Id))).Should().Be(0);
    }

    [Fact]
    public async Task Delete_ByAnAdmin_IsRefusedWithConflict()
    {
        var admin = await fixture.CreateUserAsync(null, RoleConstants.User, RoleConstants.Admin);

        var act = () => SendAsync(new DeleteMyAccountCommand(admin.Id, IdentityPostgresFixture.Password));

        await act.Should().ThrowAsync<ConflictException>();
        (await ReloadAsync(admin.Id)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_ForAPasswordlessAccount_NeedsTheSmsCode_AndRejectsAWrongOne()
    {
        var phone = $"+2010{Random.Shared.Next(10000000, 99999999)}";
        var user = await CreatePasswordlessUserAsync(phone);

        var noCode = () => SendAsync(new DeleteMyAccountCommand(user.Id, "anything"));
        await noCode.Should().ThrowAsync<BadRequestException>("a password cannot confirm an account that has none");

        await SendAsync(new SendAccountDeletionCodeCommand(user.Id));
        var sms = fixture.Services.GetRequiredService<RecordingSmsService>();
        var code = sms.Sent.Last(s => s.Phone == phone).Code;

        var wrong = () => SendAsync(new DeleteMyAccountCommand(user.Id, null, code == "000000" ? "111111" : "000000"));
        await wrong.Should().ThrowAsync<BadRequestException>();
        (await ReloadAsync(user.Id)).IsActive.Should().BeTrue();

        await SendAsync(new DeleteMyAccountCommand(user.Id, null, code));

        (await ReloadAsync(user.Id)).IsActive.Should().BeFalse();
        (await WithDbAsync(db => db.AccountDeletions.CountAsync(x => x.Id == user.Id))).Should().Be(1);
    }

    [Fact]
    public async Task SendDeletionCode_ForAccountWithAPassword_IsRejected()
    {
        var user = await fixture.CreateUserAsync("+201055500011");

        var act = () => SendAsync(new SendAccountDeletionCodeCommand(user.Id));

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public void AccountDeletion_RaisesTheUserDeletedIntegrationEvent_WithTheUserIdOnly()
    {
        var userId = Guid.NewGuid();

        var deletion = AccountDeletion.Create(userId, DateTime.UtcNow);

        deletion.Id.Should().Be(userId);
        deletion.IntegrationEvents.Should().ContainSingle().Which.Should().BeOfType<UserDeletedIntegrationEvent>()
            .Which.UserId.Should().Be(userId);
    }

    // ----- export ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Export_ReturnsTheCallersDataWithoutSecrets()
    {
        var user = await fixture.CreateUserAsync("+201066600022");
        await AddGoogleLoginAsync(user);
        await SignInAsync(user);

        var export = await SendAsync(new ExportMyDataQuery(user.Id));

        export.Profile.Id.Should().Be(user.Id);
        export.Contact.Email.Should().Be(user.Email);
        export.Contact.PhoneNumber.Should().Be("+201066600022");
        export.Roles.Should().Contain(RoleConstants.User);
        export.ExternalLogins.Should().ContainSingle().Which.Provider.Should().Be("Google");
        export.ActiveSessions.Should().ContainSingle().Which.IpAddress.Should().Be("10.0.0.1");
        export.Security.HasPassword.Should().BeTrue();

        var json = System.Text.Json.JsonSerializer.Serialize(export);
        json.Should().NotContainEquivalentOf("passwordhash").And.NotContainEquivalentOf("securitystamp")
            .And.NotContainEquivalentOf("authenticatorkey");
    }

    [Fact]
    public async Task Export_ContainsOnlyTheCallersData()
    {
        var alice = await fixture.CreateUserAsync();
        var bob = await fixture.CreateUserAsync();
        await SignInAsync(bob);

        var export = await SendAsync(new ExportMyDataQuery(alice.Id));

        export.Profile.Id.Should().Be(alice.Id);
        export.ActiveSessions.Should().BeEmpty();
        export.Contact.Email.Should().NotBe(bob.Email);
    }

    [Fact]
    public async Task Export_OfAnErasedAccount_IsRefused()
    {
        var user = await fixture.CreateUserAsync();
        await SendAsync(new DeleteMyAccountCommand(user.Id, IdentityPostgresFixture.Password));

        var act = () => SendAsync(new ExportMyDataQuery(user.Id));

        await act.Should().ThrowAsync<UnauthorizedException>();
    }
}
