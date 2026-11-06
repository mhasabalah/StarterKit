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
using Npgsql;
using Shared.Core.Authorization;

namespace Api.E2ETests.Identity;

/// <summary>
/// Real-token helpers for the auth E2E tests: users are seeded through <see cref="UserManager{TUser}"/>, every
/// session comes from the real <c>/api/auth/*</c> endpoints, and every check sends the real access token to the
/// app's JwtBearer scheme (signature, blacklist, <c>sid</c> and security-stamp checks all run).
/// </summary>
public sealed class AuthFlow(E2ETestFixture fixture)
{
    public const string Password = "E2e!Passw0rd";
    public const string NewPassword = "E2e!N3wPassw0rd";

    /// <summary>Any authenticated, non-admin endpoint: 200 while the token is alive, 401 once revoked.</summary>
    public const string ProbeUrl = "/api/v1/me/sessions";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors, string? ErrorCode);

    public sealed record Tokens(string AccessToken, string RefreshToken)
    {
        public Guid SessionId => Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(AccessToken)
            .Claims.First(c => c.Type == "sid").Value);
    }

    public sealed record SeededUser(Guid Id, string Email, string? Phone);

    private sealed record TokenData(string? AccessToken, string? RefreshToken);

    public HttpClient Anonymous() => fixture.CreateAnonymousClient();

    public HttpClient Bearer(string accessToken) => fixture.Factory.CreateClientWithToken(accessToken);

    /// <summary>A confirmed, active, complete user with <see cref="Password"/>; optionally a VERIFIED phone.</summary>
    public Task<SeededUser> SeedUserAsync(bool withVerifiedPhone = false) =>
        fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var email = $"auth-{Guid.NewGuid():N}@example.com";
            // Unique E.164 (+2010xxxxxxxx, Egyptian mobile format the normalizer accepts).
            var phone = withVerifiedPhone ? $"+2010{Random.Shared.NextInt64(10_000_000, 99_999_999)}" : null;
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"auth{Guid.NewGuid():N}",
                Email = email,
                EmailConfirmed = true,
                IsActive = true,
                IsProfileComplete = true,
                PhoneNumber = phone,
                PhoneNumberConfirmed = phone is not null,
                IsPhoneNumberVerified = phone is not null
            };
            (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, [RoleConstants.User])).Succeeded.Should().BeTrue();
            return new SeededUser(user.Id, email, phone);
        });

    public async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body) =>
        await client.PostAsJsonAsync(url, body);

    public async Task<T> PostOkAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<Envelope<T>>(text, Json)!.Data!;
    }

    public async Task<Tokens> LoginAsync(string identifier, string password = Password)
    {
        var data = await PostOkAsync<TokenData>(Anonymous(), "/api/auth/login", new { identifier, password });
        data.AccessToken.Should().NotBeNullOrEmpty();
        return new Tokens(data.AccessToken!, data.RefreshToken!);
    }

    public Task<HttpResponseMessage> TryLoginAsync(string identifier, string password) =>
        Anonymous().PostAsJsonAsync("/api/auth/login", new { identifier, password });

    public async Task<Tokens> RefreshAsync(string refreshToken)
    {
        var data = await PostOkAsync<TokenData>(Anonymous(), "/api/auth/refresh", new { refreshToken });
        return new Tokens(data.AccessToken!, data.RefreshToken!);
    }

    public async Task<HttpStatusCode> TryRefreshAsync(string refreshToken)
    {
        using var response = await Anonymous().PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        return response.StatusCode;
    }

    /// <summary>
    /// The arguments of every Notification dispatch job addressed to <paramref name="email"/>, i.e. what the mail would
    /// contain (links included). Jobs are written to Hangfire storage synchronously when the request enqueues them,
    /// whatever state a worker moves them to afterwards.
    /// </summary>
    /// <remarks>
    /// Reads <c>hangfire.job</c> in ONE query. The monitoring API lists one state at a time, so a job a worker moved
    /// between two of its reads was missed (or counted twice) - a flaky race, not a missing mail.
    /// </remarks>
    public Task<List<string>> QueuedMailsToAsync(string email) => fixture.WithScopeAsync(async sp =>
    {
        var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(E2ETestFactory.ConnectionStringName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT arguments::text FROM hangfire.job WHERE invocationdata::text LIKE '%NotificationDispatchJob%'", connection);

        var mails = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            // arguments = JSON array of the serialized arguments; [0] is the NotificationMessage.
            var args = JsonSerializer.Deserialize<string[]>(reader.GetString(0));
            if (args is { Length: > 0 } && args[0].Contains(email, StringComparison.OrdinalIgnoreCase))
                mails.Add(args[0]);
        }

        return mails;
    });

    /// <summary>Status of an authenticated GET with this access token (200 alive, 401 revoked).</summary>
    public async Task<HttpStatusCode> ProbeAsync(string accessToken)
    {
        using var response = await Bearer(accessToken).GetAsync(ProbeUrl);
        return response.StatusCode;
    }
}
