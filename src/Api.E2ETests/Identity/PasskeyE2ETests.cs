using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// Passkeys over real HTTP with a software authenticator and the REAL WebAuthn verifier: registration with step-up,
/// usernameless sign-in yielding an MFA session, single-use ceremonies, forged signatures, ownership, last-method
/// protection and the audit trail.
/// </summary>
[Collection(E2ECollection.Name)]
public class PasskeyE2ETests(E2ETestFixture fixture)
{
    private const string Base = "/api/auth/passkeys";
    private const string Password = "E2e!Passw0rd";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors, string? ErrorCode);

    private sealed record LoginData(string? AccessToken, string? RefreshToken, bool RequiresTwoFactor);

    private sealed record OptionsData(string CeremonyId, string OptionsJson, DateTime ExpiresAt);

    private sealed record PasskeyData(string Id, string? Name, bool IsBackedUp, bool IsUserVerified, List<string> Transports);

    [Fact]
    public async Task Register_then_sign_in_with_the_passkey_gives_an_mfa_session()
    {
        var (_, signedIn) = await SignedInUserAsync();
        using var authenticator = new SoftwareAuthenticator();

        var passkey = await RegisterAsync(signedIn, authenticator, "Work laptop");
        passkey.Name.Should().Be("Work laptop");
        passkey.IsUserVerified.Should().BeTrue();
        passkey.IsBackedUp.Should().BeTrue();
        passkey.Id.Should().Be(authenticator.CredentialIdBase64Url);

        var list = await GetAsync<List<PasskeyData>>(signedIn, Base);
        list.Should().ContainSingle(p => p.Id == passkey.Id);

        var login = await SignInAsync(authenticator);
        login.AccessToken.Should().NotBeNullOrEmpty();
        login.RequiresTwoFactor.Should().BeFalse();
        Amr(login.AccessToken!).Should().Contain(AuthenticationMethods.Mfa);

        // The new session is a real one.
        var viaPasskey = fixture.Factory.CreateClientWithToken(login.AccessToken!);
        (await viaPasskey.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_sign_in_ceremony_is_single_use()
    {
        var (_, signedIn) = await SignedInUserAsync();
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(signedIn, authenticator);

        var anonymous = fixture.CreateAnonymousClient();
        var options = await PostAsync<OptionsData>(anonymous, $"{Base}/assertion/options", new { });
        var assertion = authenticator.Get(options.OptionsJson);

        (await anonymous.PostAsJsonAsync($"{Base}/assertion", new { options.CeremonyId, credential = assertion }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await anonymous.PostAsJsonAsync($"{Base}/assertion", new { options.CeremonyId, credential = assertion });
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_forged_signature_or_unknown_credential_is_rejected()
    {
        var (_, signedIn) = await SignedInUserAsync();
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(signedIn, authenticator);
        var anonymous = fixture.CreateAnonymousClient();

        var forgedOptions = await PostAsync<OptionsData>(anonymous, $"{Base}/assertion/options", new { });
        var forged = authenticator.Get(forgedOptions.OptionsJson, overrideSignature: new byte[70]);
        (await anonymous.PostAsJsonAsync($"{Base}/assertion", new { forgedOptions.CeremonyId, credential = forged }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var stranger = new SoftwareAuthenticator();
        var strangerOptions = await PostAsync<OptionsData>(anonymous, $"{Base}/assertion/options", new { });
        var unknown = stranger.Get(strangerOptions.OptionsJson);
        (await anonymous.PostAsJsonAsync($"{Base}/assertion", new { strangerOptions.CeremonyId, credential = unknown }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_credential_from_another_origin_is_rejected()
    {
        var (_, signedIn) = await SignedInUserAsync();
        using var phishing = new SoftwareAuthenticator(origin: "https://evil.example");

        var options = await PostAsync<OptionsData>(signedIn, $"{Base}/registration/options", new { });
        var response = await signedIn.PostAsJsonAsync($"{Base}/registration",
            new { options.CeremonyId, credential = phishing.Create(options.OptionsJson) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync<object>(response)).ErrorCode.Should().Be("passkey_invalid");
    }

    [Fact]
    public async Task A_registration_ceremony_cannot_be_finished_by_another_account()
    {
        var (_, alice) = await SignedInUserAsync();
        var (_, mallory) = await SignedInUserAsync();
        using var authenticator = new SoftwareAuthenticator();

        var options = await PostAsync<OptionsData>(alice, $"{Base}/registration/options", new { });
        var response = await mallory.PostAsJsonAsync($"{Base}/registration",
            new { options.CeremonyId, credential = authenticator.Create(options.OptionsJson) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Adding_a_passkey_needs_a_fresh_session()
    {
        // A real account, but a caller without a known session (no sid): treated as "not freshly signed in".
        var (userId, _) = await SignedInUserAsync();
        var stale = fixture.CreateClientAs(userId, RoleConstants.User);

        var response = await stale.PostAsJsonAsync($"{Base}/registration/options", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync<object>(response)).ErrorCode.Should().Be("reauthentication_required");
    }

    [Fact]
    public async Task Rename_and_delete_are_scoped_to_the_owner()
    {
        var (_, owner) = await SignedInUserAsync();
        var (_, other) = await SignedInUserAsync();
        using var authenticator = new SoftwareAuthenticator();
        var passkey = await RegisterAsync(owner, authenticator);

        (await other.PatchAsJsonAsync($"{Base}/{passkey.Id}", new { name = "mine now" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Base}/{passkey.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var renamed = await ReadAsync<PasskeyData>(await owner.PatchAsJsonAsync($"{Base}/{passkey.Id}", new { name = "YubiKey" }));
        renamed.Data!.Name.Should().Be("YubiKey");

        // The owner has a password, so the passkey is not the last way in.
        (await owner.DeleteAsync($"{Base}/{passkey.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync<List<PasskeyData>>(owner, Base)).Should().BeEmpty();
        (await owner.DeleteAsync($"{Base}/{passkey.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_last_sign_in_method_cannot_be_removed()
    {
        var (userId, signedIn) = await SignedInUserAsync();
        using var authenticator = new SoftwareAuthenticator();
        var passkey = await RegisterAsync(signedIn, authenticator);

        // Go passwordless: the passkey becomes the only way in.
        await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(userId.ToString()))!;
            (await users.RemovePasswordAsync(user)).Succeeded.Should().BeTrue();
        });

        var login = await SignInAsync(authenticator);
        var client = fixture.Factory.CreateClientWithToken(login.AccessToken!);

        var response = await client.DeleteAsync($"{Base}/{passkey.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync<object>(response)).ErrorCode.Should().Be("last_sign_in_method");
    }

    [Fact]
    public async Task A_deactivated_account_cannot_sign_in_with_its_passkey()
    {
        var (userId, signedIn) = await SignedInUserAsync();
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(signedIn, authenticator);

        await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(userId.ToString()))!;
            user.IsActive = false;
            await users.UpdateAsync(user);
        });

        var anonymous = fixture.CreateAnonymousClient();
        var options = await PostAsync<OptionsData>(anonymous, $"{Base}/assertion/options", new { });
        var response = await anonymous.PostAsJsonAsync($"{Base}/assertion",
            new { options.CeremonyId, credential = authenticator.Get(options.OptionsJson) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Api_keys_cannot_manage_passkeys()
    {
        var anonymous = fixture.CreateAnonymousClient();
        anonymous.DefaultRequestHeaders.Add(global::Identity.Services.ApiKeys.ApiKeyRequest.HeaderName, "sk_invalid");

        (await anonymous.GetAsync(Base)).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private async Task<(Guid UserId, HttpClient Client)> SignedInUserAsync()
    {
        var email = $"passkey-{Guid.NewGuid():N}@example.com";
        var userId = await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = $"pk{Guid.NewGuid():N}", Email = email, EmailConfirmed = true,
                IsActive = true, IsProfileComplete = true, FirstName = "Pat", LastName = "Key"
            };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            (await users.AddToRoleAsync(user, RoleConstants.User)).Succeeded.Should().BeTrue();
            return user.Id;
        });

        var login = await PostAsync<LoginData>(fixture.CreateAnonymousClient(), "/api/auth/login",
            new { identifier = email, password = Password });
        return (userId, fixture.Factory.CreateClientWithToken(login.AccessToken!));
    }

    private static async Task<PasskeyData> RegisterAsync(HttpClient client, SoftwareAuthenticator authenticator, string? name = null)
    {
        var options = await PostAsync<OptionsData>(client, $"{Base}/registration/options", new { });
        options.CeremonyId.Should().NotBeNullOrEmpty();
        JsonNode.Parse(options.OptionsJson)!["authenticatorSelection"]?["userVerification"]?.GetValue<string>()
            .Should().Be("required");

        return await PostAsync<PasskeyData>(client, $"{Base}/registration",
            new { options.CeremonyId, credential = authenticator.Create(options.OptionsJson), name });
    }

    private async Task<LoginData> SignInAsync(SoftwareAuthenticator authenticator)
    {
        var anonymous = fixture.CreateAnonymousClient();
        var options = await PostAsync<OptionsData>(anonymous, $"{Base}/assertion/options", new { });
        return await PostAsync<LoginData>(anonymous, $"{Base}/assertion",
            new { options.CeremonyId, credential = authenticator.Get(options.OptionsJson) });
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<Envelope<T>>(text, Json)!.Data!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<Envelope<T>>(text, Json)!.Data!;
    }

    private static async Task<Envelope<T>> ReadAsync<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<Envelope<T>>(await response.Content.ReadAsStringAsync(), Json)!;

    private static IEnumerable<string> Amr(string accessToken) => new JwtSecurityTokenHandler().ReadJwtToken(accessToken)
        .Claims.Where(c => c.Type == AuthenticationMethods.ClaimType).Select(c => c.Value);
}
