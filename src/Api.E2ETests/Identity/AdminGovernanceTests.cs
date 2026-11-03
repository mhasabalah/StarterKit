using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Xunit;
using Totp = Identity.Tests.Totp;

namespace Api.E2ETests.Identity;

/// <summary>
/// Admin governance over real HTTP: permission policies per role, the mfa_required 403 in Enforce mode, and a full
/// password + TOTP login through the REAL JWT pipeline (no test auth headers).
/// </summary>
[Collection(E2ECollection.Name)]
public class AdminGovernanceTests(E2ETestFixture fixture)
{
    private const string IdentityAudit = "/api/v1/admin/identity/audit";
    private const string PendingSubscriptions = "/api/v1/admin/subscriptions/pending";
    private const string Users = "/api/identity/users";
    private const string Password = "E2e!Passw0rd";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors);

    private sealed record LoginData(
        string? AccessToken, string? RefreshToken, bool RequiresTwoFactor, string? TwoFactorChallengeToken);

    private sealed record SetupData(string SharedKey, string AuthenticatorUri);

    private sealed record EnableData(List<string> RecoveryCodes, string AccessToken, string RefreshToken);

    private sealed record AuditRow(Guid AdminId, string Action, Guid? TargetId);

    private sealed record Page<T>(List<T>? Result);

    private HttpClient As(string role, bool mfa = false)
    {
        var client = fixture.CreateClientAs(Guid.NewGuid(), role);
        if (mfa) client.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");
        return client;
    }

    /// <summary>Switches Identity:TwoFactor:EnforcementMode for the shared host (E2E tests run sequentially).</summary>
    private async Task WithEnforcementAsync(string mode, Func<Task> body)
    {
        var configuration = (IConfigurationRoot)fixture.Factory.Services.GetRequiredService<IConfiguration>();
        configuration["Identity:TwoFactor:EnforcementMode"] = mode;
        configuration.Reload();
        try
        {
            await body();
        }
        finally
        {
            configuration["Identity:TwoFactor:EnforcementMode"] = "Warn";
            configuration.Reload();
        }
    }

    [Fact]
    public async Task Permissions_follow_the_role_map()
    {
        (await As(RoleConstants.Finance).GetAsync(PendingSubscriptions)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await As(RoleConstants.Finance).GetAsync(IdentityAudit)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await As(RoleConstants.Support).GetAsync(PendingSubscriptions)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await As(RoleConstants.Support).GetAsync(Users)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await As(RoleConstants.Support).PostAsJsonAsync($"/api/v1/admin/subscriptions/{Guid.NewGuid()}/approve", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "Support has billing.read but not billing.approve");
        (await As(RoleConstants.Support).GetAsync(IdentityAudit)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As(RoleConstants.Finance).GetAsync(Users)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As(RoleConstants.User).GetAsync(Users)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.CreateAnonymousClient().GetAsync(IdentityAudit)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Warn_mode_lets_privileged_callers_without_mfa_through_but_flags_the_response()
    {
        var response = await As(RoleConstants.Finance).GetAsync(IdentityAudit);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues(AuthorizationErrorCodes.HeaderName).Should().Contain(AuthorizationErrorCodes.MfaRequired);
    }

    [Fact]
    public async Task Enforce_mode_returns_mfa_required_until_the_session_passed_2fa() =>
        await WithEnforcementAsync("Enforce", async () =>
        {
            var withoutMfa = await As(RoleConstants.Finance).GetAsync(IdentityAudit);
            withoutMfa.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            withoutMfa.Headers.GetValues(AuthorizationErrorCodes.HeaderName).Should().Contain(AuthorizationErrorCodes.MfaRequired);
            var body = await withoutMfa.Content.ReadFromJsonAsync<Envelope<object>>(Json);
            body!.Errors.Should().NotBeNull();
            body.Errors![0].Should().Be(AuthorizationErrorCodes.MfaRequired);

            (await As(RoleConstants.Finance, mfa: true).GetAsync(IdentityAudit)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await As(RoleConstants.Admin).GetAsync(Users)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // Support is not in RequiredForRoles; a missing permission is a plain 403, never mfa_required.
            (await As(RoleConstants.Support).GetAsync(PendingSubscriptions)).StatusCode.Should().Be(HttpStatusCode.OK);
            var noPermission = await As(RoleConstants.Finance).GetAsync(Users);
            noPermission.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            noPermission.Headers.Contains(AuthorizationErrorCodes.HeaderName).Should().BeFalse();
        });

    [Fact]
    public async Task Real_login_with_totp_two_factor_end_to_end()
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        var userId = await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = $"admin{Guid.NewGuid():N}", Email = email, EmailConfirmed = true,
                IsActive = true, IsProfileComplete = true
            };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, [RoleConstants.User, RoleConstants.Admin])).Succeeded.Should().BeTrue();
            return user.Id;
        });
        var anonymous = fixture.CreateAnonymousClient();

        // 1. Password-only login (no 2FA yet) gives a normal session.
        var first = await PostAsync<LoginData>(anonymous, "/api/auth/login", new { identifier = email, password = Password });
        first.AccessToken.Should().NotBeNull();
        Amr(first.AccessToken!).Should().NotContain(AuthenticationMethods.Mfa);
        var signedIn = fixture.Factory.CreateClientWithToken(first.AccessToken!);

        // 2. Enroll: setup -> enable with a code from the key (what the authenticator app does).
        var setup = await PostAsync<SetupData>(signedIn, "/api/auth/2fa/setup", new { });
        setup.AuthenticatorUri.Should().StartWith("otpauth://totp/");
        var key = setup.SharedKey.Replace(" ", string.Empty).ToUpperInvariant();
        var enabled = await PostAsync<EnableData>(signedIn, "/api/auth/2fa/enable", new { code = Totp.Code(key) });
        enabled.RecoveryCodes.Should().NotBeEmpty();
        Amr(enabled.AccessToken).Should().Contain(AuthenticationMethods.Mfa);

        // The pre-2FA token was revoked by the enable step.
        (await signedIn.GetAsync(Users)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 3. Password login now only yields a challenge, never tokens.
        var challenged = await PostAsync<LoginData>(anonymous, "/api/auth/login", new { identifier = email, password = Password });
        challenged.RequiresTwoFactor.Should().BeTrue();
        challenged.AccessToken.Should().BeNull();
        challenged.TwoFactorChallengeToken.Should().NotBeNullOrEmpty();

        // The challenge is not an access token.
        (await fixture.Factory.CreateClientWithToken(challenged.TwoFactorChallengeToken!).GetAsync(Users))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var wrong = await anonymous.PostAsJsonAsync("/api/auth/2fa/verify",
            new { challengeToken = challenged.TwoFactorChallengeToken, code = Totp.WrongCode(key) });
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 4. Verify with the authenticator code -> MFA session (a code other than the one enable consumed).
        var code = Totp.Code(key, 1);
        var verified = await PostAsync<LoginData>(anonymous, "/api/auth/2fa/verify",
            new { challengeToken = challenged.TwoFactorChallengeToken, code });
        verified.AccessToken.Should().NotBeNull();
        Amr(verified.AccessToken!).Should().Contain(AuthenticationMethods.Mfa);

        // The same code cannot be replayed inside its validity window.
        var replay = await anonymous.PostAsJsonAsync("/api/auth/2fa/verify",
            new { challengeToken = challenged.TwoFactorChallengeToken, code });
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 5. The real token passes the permission + MFA policy even in Enforce mode, and the enable is audited.
        await WithEnforcementAsync("Enforce", async () =>
        {
            var admin = fixture.Factory.CreateClientWithToken(verified.AccessToken!);
            var audit = await admin.GetAsync($"{IdentityAudit}?targetId={userId}&pageSize=50");
            audit.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = (await audit.Content.ReadFromJsonAsync<Envelope<Page<AuditRow>>>(Json))!.Data!;
            page.Result.Should().Contain(r => r.Action == "2fa.enabled" && r.AdminId == userId);
        });
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<Envelope<T>>(text, Json)!.Data!;
    }

    private static IEnumerable<string> Amr(string accessToken) => new JwtSecurityTokenHandler().ReadJwtToken(accessToken)
        .Claims.Where(c => c.Type == AuthenticationMethods.ClaimType).Select(c => c.Value);
}
