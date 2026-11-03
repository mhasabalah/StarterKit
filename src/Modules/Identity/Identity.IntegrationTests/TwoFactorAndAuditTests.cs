using System.IdentityModel.Tokens.Jwt;
using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Users.Commands;
using Identity.Data;
using Identity.Domains.Audit.Models;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Models;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;
using Shared.Core.Exceptions;
using Totp = Identity.Tests.Totp;

namespace Identity.IntegrationTests;

/// <summary>
/// The full 2FA login and the admin audit against PostgreSQL: real UserManager/SignInManager, the encrypted
/// authenticator key store, the MediatR pipeline with the audited-command transaction behavior.
/// </summary>
[Collection(IdentityPostgresCollection.Name)]
public sealed class TwoFactorAndAuditTests(IdentityPostgresFixture fixture)
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

    /// <summary>Setup + enable through the real handlers; returns the base32 key (what the authenticator app holds).</summary>
    private async Task<(string Key, EnableTwoFactorResult Enabled)> EnrollAsync(ApplicationUser user)
    {
        var setup = await SendAsync(new SetupTwoFactorCommand(user.Id));
        var key = setup.SharedKey.Replace(" ", string.Empty).ToUpperInvariant();
        var enabled = await SendAsync(new EnableTwoFactorCommand(user.Id, Totp.Code(key)));
        return (key, enabled);
    }

    private static IEnumerable<string> Amr(string accessToken) => new JwtSecurityTokenHandler().ReadJwtToken(accessToken)
        .Claims.Where(c => c.Type == AuthenticationMethods.ClaimType).Select(c => c.Value);

    [Fact]
    public async Task Login_with_2fa_goes_through_a_challenge_and_verify_issues_an_mfa_session_that_refresh_keeps()
    {
        var admin = await fixture.CreateUserAsync(null, RoleConstants.User, RoleConstants.Admin);
        var (key, enabled) = await EnrollAsync(admin);
        Amr(enabled.AccessToken).Should().Contain(AuthenticationMethods.Mfa, "enabling 2FA returns an MFA session");

        var login = await SendAsync(new LoginCommand(admin.Email!, IdentityPostgresFixture.Password));

        login.RequiresTwoFactor.Should().BeTrue();
        login.AccessToken.Should().BeNull();
        login.TwoFactorChallengeToken.Should().NotBeNullOrEmpty();

        var verified = await SendAsync(new VerifyTwoFactorCommand(login.TwoFactorChallengeToken!, Totp.Code(key, 1), null));

        verified.AccessToken.Should().NotBeNull();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(verified.AccessToken);
        token.Claims.Where(c => c.Type == AuthenticationMethods.ClaimType).Select(c => c.Value)
            .Should().Contain(AuthenticationMethods.Mfa);
        token.Claims.Should().NotContain(c => c.Type == Permissions.ClaimType, "tokens carry roles only");
        verified.User.Permissions.Should().Contain(Permissions.UsersManage).And.NotContain(Permissions.AdminsManage);

        var row = await WithDbAsync(db => db.RefreshTokens.AsNoTracking()
            .SingleAsync(t => t.TokenHash == Identity.Services.Token.TokenHasher.Hash(verified.RefreshToken!)));
        row.MultiFactor.Should().BeTrue();

        await using var scope = fixture.NewScope();
        var refreshed = await scope.ServiceProvider.GetRequiredService<IAuthSessionService>()
            .RefreshAsync(verified.RefreshToken!, "127.0.0.1");
        Amr(refreshed.AccessToken).Should().Contain(AuthenticationMethods.Mfa, "refresh keeps the MFA flag");
    }

    [Fact]
    public async Task Wrong_codes_are_persisted_strikes_and_lock_the_account()
    {
        var user = await fixture.CreateUserAsync();
        var (key, _) = await EnrollAsync(user);
        var login = await SendAsync(new LoginCommand(user.Email!, IdentityPostgresFixture.Password));

        var wrong = Totp.WrongCode(key);
        await FluentActions.Awaiting(() => SendAsync(new VerifyTwoFactorCommand(login.TwoFactorChallengeToken!, wrong, null)))
            .Should().ThrowAsync<UnauthorizedException>();
        (await ReloadAsync(user.Id)).AccessFailedCount.Should().Be(1, "the strike survives the rejected request");

        for (var i = 1; i < IdentityPostgresFixture.MaxFailedAccessAttempts; i++)
        {
            await FluentActions.Awaiting(() => SendAsync(new VerifyTwoFactorCommand(login.TwoFactorChallengeToken!, wrong, null)))
                .Should().ThrowAsync<UnauthorizedException>();
        }

        var locked = await ReloadAsync(user.Id);
        locked.LockoutEnd.Should().BeAfter(DateTimeOffset.UtcNow);
        await FluentActions.Awaiting(() => SendAsync(
                new VerifyTwoFactorCommand(login.TwoFactorChallengeToken!, Totp.Code(key, 1), null)))
            .Should().ThrowAsync<UnauthorizedException>().WithMessage(TwoFactorService.LockedOutMessage);
    }

    [Fact]
    public async Task Admin_mutation_and_its_audit_row_are_committed_together()
    {
        var superAdmin = await fixture.CreateUserAsync(null, RoleConstants.User, RoleConstants.Admin, RoleConstants.SuperAdmin);
        var target = await fixture.CreateUserAsync();
        fixture.CurrentUserId = superAdmin.Id;

        await SendAsync(new LockUserCommand
        {
            UserId = target.Id, Reason = "integration test lock", Duration = LockDuration.OneDay, LockedByAdminId = superAdmin.Id
        });

        var rows = await WithDbAsync(db => db.AdminAuditLogs.AsNoTracking().Where(a => a.TargetId == target.Id).ToListAsync());
        var row = rows.Should().ContainSingle().Which;
        row.Action.Should().Be(IdentityAuditActions.UserLocked);
        row.AdminId.Should().Be(superAdmin.Id);
        row.Reason.Should().Be("integration test lock");
        row.IpAddress.Should().Be("127.0.0.1");
        row.UserAgent.Should().Be("integration-tests");
        row.BeforeJson.Should().Contain("\"isActive\": true", "jsonb normalizes the JSON");
        row.AfterJson.Should().Contain("\"isActive\": false");
        (await ReloadAsync(target.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_rejected_admin_mutation_changes_nothing_and_writes_no_audit_row()
    {
        var admin = await fixture.CreateUserAsync(null, RoleConstants.User, RoleConstants.Admin);
        var superAdmin = await fixture.CreateUserAsync(null, RoleConstants.User, RoleConstants.SuperAdmin);
        fixture.CurrentUserId = admin.Id;

        await FluentActions.Awaiting(() => SendAsync(new LockUserCommand
            {
                UserId = superAdmin.Id, Reason = "admin cannot lock a superadmin", LockedByAdminId = admin.Id
            }))
            .Should().ThrowAsync<ForbiddenException>();

        (await WithDbAsync(db => db.AdminAuditLogs.CountAsync(a => a.TargetId == superAdmin.Id))).Should().Be(0);
        (await ReloadAsync(superAdmin.Id)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Two_factor_lifecycle_is_audited_without_secrets_and_a_superadmin_reset_ends_sessions()
    {
        var superAdmin = await fixture.CreateUserAsync(null, RoleConstants.User, RoleConstants.Admin, RoleConstants.SuperAdmin);
        var finance = await fixture.CreateUserAsync(null, RoleConstants.User, RoleConstants.Finance);
        var (key, enabled) = await EnrollAsync(finance);

        var storedKey = await WithDbAsync(db => db.UserTokens.AsNoTracking()
            .Where(t => t.UserId == finance.Id && t.Name == ProtectedUserStore.AuthenticatorKeyTokenName)
            .Select(t => t.Value).SingleAsync());
        storedKey.Should().NotContain(key, "the authenticator key is encrypted at rest");

        fixture.CurrentUserId = superAdmin.Id;
        await SendAsync(new ResetUserTwoFactorCommand
        {
            UserId = finance.Id, Reason = "lost phone, verified by call", ResetByAdminId = superAdmin.Id
        });

        var reset = await ReloadAsync(finance.Id);
        reset.TwoFactorEnabled.Should().BeFalse();
        (await WithDbAsync(db => db.RefreshTokens.AsNoTracking()
            .CountAsync(t => t.UserId == finance.Id && t.RevokedAt == null))).Should().Be(0);

        var rows = await WithDbAsync(db => db.AdminAuditLogs.AsNoTracking()
            .Where(a => a.TargetId == finance.Id).OrderBy(a => a.OccurredAt).ToListAsync());
        rows.Select(r => r.Action).Should().Equal(IdentityAuditActions.TwoFactorEnabled, IdentityAuditActions.TwoFactorReset);
        rows[1].AdminId.Should().Be(superAdmin.Id);
        var json = string.Concat(rows.Select(r => r.BeforeJson + r.AfterJson));
        json.Should().NotContain(key);
        foreach (var code in enabled.RecoveryCodes) json.Should().NotContain(code);
    }
}
