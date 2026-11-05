using System.Net;
using System.Net.Http.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// The session lifecycle over real HTTP with real tokens: refresh rotation and reuse detection, logout (one session,
/// all devices), change-password and password-login lockout. Unit/integration tests cover the services; these prove
/// the endpoints, the JwtBearer revocation checks and the database agree end to end.
/// </summary>
[Collection(E2ECollection.Name)]
public class SessionLifecycleE2ETests(E2ETestFixture fixture)
{
    private readonly AuthFlow _auth = new(fixture);

    [Fact]
    public async Task Refresh_rotates_the_pair_within_the_same_session()
    {
        var user = await _auth.SeedUserAsync();
        var login = await _auth.LoginAsync(user.Email);

        var rotated = await _auth.RefreshAsync(login.RefreshToken);

        rotated.RefreshToken.Should().NotBe(login.RefreshToken);
        rotated.SessionId.Should().Be(login.SessionId, "a refresh continues the session (refresh-token family)");
        (await _auth.ProbeAsync(rotated.AccessToken)).Should().Be(HttpStatusCode.OK);
        // The previous access token was not revoked by a plain refresh; it simply expires.
        (await _auth.ProbeAsync(login.AccessToken)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reusing_a_rotated_refresh_token_revokes_the_whole_session()
    {
        var user = await _auth.SeedUserAsync();
        var login = await _auth.LoginAsync(user.Email);
        var rotated = await _auth.RefreshAsync(login.RefreshToken);

        // A stolen copy of the old refresh token is presented again.
        (await _auth.TryRefreshAsync(login.RefreshToken)).Should().Be(HttpStatusCode.Unauthorized);

        // The legitimate holder's descendant is revoked too: the family is burned.
        (await _auth.TryRefreshAsync(rotated.RefreshToken)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Garbage_refresh_token_is_rejected()
    {
        (await _auth.TryRefreshAsync("not-a-real-refresh-token")).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ends_the_whole_session_including_its_other_access_tokens()
    {
        var user = await _auth.SeedUserAsync();
        var login = await _auth.LoginAsync(user.Email);
        var rotated = await _auth.RefreshAsync(login.RefreshToken); // second access token of the SAME session
        var otherDevice = await _auth.LoginAsync(user.Email);

        using var response = await _auth.PostAsync(_auth.Bearer(rotated.AccessToken), "/api/auth/logout",
            new { refreshToken = rotated.RefreshToken });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await _auth.ProbeAsync(rotated.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
        (await _auth.TryRefreshAsync(rotated.RefreshToken)).Should().Be(HttpStatusCode.Unauthorized);
        (await _auth.ProbeAsync(login.AccessToken)).Should().Be(HttpStatusCode.Unauthorized,
            "an earlier access token of the logged-out session carries the same sid");

        // Another device is a different session and keeps working.
        (await _auth.ProbeAsync(otherDevice.AccessToken)).Should().Be(HttpStatusCode.OK);
        (await _auth.TryRefreshAsync(otherDevice.RefreshToken)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_from_all_devices_ends_every_session()
    {
        var user = await _auth.SeedUserAsync();
        var phone = await _auth.LoginAsync(user.Email);
        var laptop = await _auth.LoginAsync(user.Email);

        using var response = await _auth.PostAsync(_auth.Bearer(laptop.AccessToken), "/api/auth/logout",
            new { refreshToken = laptop.RefreshToken, logoutAllDevices = true });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var session in new[] { phone, laptop })
        {
            (await _auth.ProbeAsync(session.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
            (await _auth.TryRefreshAsync(session.RefreshToken)).Should().Be(HttpStatusCode.Unauthorized);
        }

        // Signing in again works: logout is not a lock.
        (await _auth.ProbeAsync((await _auth.LoginAsync(user.Email)).AccessToken)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_requires_a_valid_token()
    {
        using var response = await _auth.PostAsync(_auth.Anonymous(), "/api/auth/logout", new { });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_ends_every_other_session_and_returns_a_working_one()
    {
        var user = await _auth.SeedUserAsync();
        var current = await _auth.LoginAsync(user.Email);
        var otherDevice = await _auth.LoginAsync(user.Email);

        var changed = await _auth.PostOkAsync<AuthFlow.Tokens>(_auth.Bearer(current.AccessToken), "/api/auth/change-password",
            new { currentPassword = AuthFlow.Password, newPassword = AuthFlow.NewPassword, confirmPassword = AuthFlow.NewPassword });

        foreach (var old in new[] { current, otherDevice })
        {
            (await _auth.ProbeAsync(old.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
            (await _auth.TryRefreshAsync(old.RefreshToken)).Should().Be(HttpStatusCode.Unauthorized);
        }

        (await _auth.ProbeAsync(changed.AccessToken)).Should().Be(HttpStatusCode.OK);
        (await _auth.TryRefreshAsync(changed.RefreshToken)).Should().Be(HttpStatusCode.OK);

        (await _auth.TryLoginAsync(user.Email, AuthFlow.Password)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _auth.LoginAsync(user.Email, AuthFlow.NewPassword);
    }

    [Fact]
    public async Task Change_password_with_a_wrong_current_password_changes_nothing()
    {
        var user = await _auth.SeedUserAsync();
        var session = await _auth.LoginAsync(user.Email);

        using var response = await _auth.PostAsync(_auth.Bearer(session.AccessToken), "/api/auth/change-password",
            new { currentPassword = "Wrong!Passw0rd", newPassword = AuthFlow.NewPassword, confirmPassword = AuthFlow.NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _auth.ProbeAsync(session.AccessToken)).Should().Be(HttpStatusCode.OK);
        await _auth.LoginAsync(user.Email); // old password still valid
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_even_against_the_right_password()
    {
        var user = await _auth.SeedUserAsync();

        // Identity:Lockout:MaxFailedAccessAttempts = 5 (appsettings.json).
        for (var i = 0; i < 5; i++)
            (await _auth.TryLoginAsync(user.Email, "Wrong!Passw0rd")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var locked = await _auth.TryLoginAsync(user.Email, AuthFlow.Password);
        locked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await locked.Content.ReadAsStringAsync()).Should().Contain("locked out");
    }

    [Fact]
    public async Task Unknown_account_and_wrong_password_get_the_same_answer()
    {
        var user = await _auth.SeedUserAsync();

        using var unknown = await _auth.TryLoginAsync($"nobody-{Guid.NewGuid():N}@example.com", AuthFlow.Password);
        using var wrong = await _auth.TryLoginAsync(user.Email, "Wrong!Passw0rd");

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var unknownBody = await unknown.Content.ReadFromJsonAsync<AuthFlow.Envelope<object>>(AuthFlow.Json);
        var wrongBody = await wrong.Content.ReadFromJsonAsync<AuthFlow.Envelope<object>>(AuthFlow.Json);
        unknownBody!.Errors.Should().Equal(wrongBody!.Errors);
    }
}
