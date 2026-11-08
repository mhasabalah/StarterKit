using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;

namespace Api.E2ETests.Infrastructure;

/// <summary>Creates real API keys over HTTP for e2e tests that check what a scoped key may reach.</summary>
public static class ApiKeyTestExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record Created(string ApiKey);

    /// <summary>A real user with the given role, plus a real key for it created through the API.</summary>
    public static async Task<string> CreateApiKeyForAsync(this E2ETestFixture fixture, string role, params string[] scopes)
    {
        var userId = await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = $"key{Guid.NewGuid():N}", Email = $"key{Guid.NewGuid():N}@example.com",
                EmailConfirmed = true, IsActive = true, IsProfileComplete = true
            };
            (await users.CreateAsync(user, "E2e!Passw0rd")).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, [RoleConstants.User, role])).Succeeded.Should().BeTrue();
            return user.Id;
        });

        using var response = await fixture.CreateClientAs(userId, RoleConstants.User, role)
            .PostAsJsonAsync("/api/v1/me/api-keys", new { name = "e2e", scopes });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Envelope<Created>>(Json);
        return body!.Data!.ApiKey;
    }
}
