using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Data;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Api.E2ETests.Identity;

/// <summary>Self-service account deletion and personal-data export over real HTTP, the real module composition and Postgres.</summary>
[Collection(E2ECollection.Name)]
public class AccountLifecycleE2ETests(E2ETestFixture fixture)
{
    private const string Profile = "/api/identity/profile";
    private const string Password = "E2e!Passw0rd";

    private async Task<ApplicationUser> CreateUserAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = $"e2e{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.com",
            PhoneNumber = $"+2010{Random.Shared.Next(10000000, 99999999)}", IsActive = true, FirstName = "Ada", LastName = "Lovelace"
        };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, RoleConstants.User);
        return user;
    }

    private static HttpRequestMessage DeleteRequest(string password) =>
        new(HttpMethod.Delete, Profile) { Content = JsonContent.Create(new { password }) };

    [Fact]
    public async Task Delete_RequiresAuthentication()
    {
        var response = await fixture.Factory.CreateAnonymousClient().SendAsync(DeleteRequest(Password));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendDeletionCode_RequiresAuthentication()
    {
        var response = await fixture.Factory.CreateAnonymousClient().PostAsync($"{Profile}/delete-code", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Export_RequiresAuthentication()
    {
        var response = await fixture.Factory.CreateAnonymousClient().GetAsync($"{Profile}/export");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_WithWrongPassword_Returns400_AndTheAccountSurvives()
    {
        var user = await CreateUserAsync();
        var client = fixture.CreateClientAs(user.Id, RoleConstants.User);

        var response = await client.SendAsync(DeleteRequest("Wrong-Passw0rd!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync($"{Profile}/export")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_WithCorrectPassword_ErasesTheAccount_AndQueuesTheUserDeletedEventInTheOutbox()
    {
        var user = await CreateUserAsync();
        var client = fixture.CreateClientAs(user.Id, RoleConstants.User);

        var response = await client.SendAsync(DeleteRequest(Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        stored.Email.Should().EndWith("@deleted.invalid");
        stored.PhoneNumber.Should().BeNull();
        stored.IsActive.Should().BeFalse();
        (await db.AccountDeletions.AnyAsync(a => a.Id == user.Id)).Should().BeTrue();

        // The outbox row proves the integration event was committed with the erasure (it may already be delivered).
        var outbox = await db.Database
            .SqlQuery<string>($"SELECT message_type AS \"Value\" FROM identity.outbox_messages WHERE payload::text LIKE {"%" + user.Id + "%"}")
            .ToListAsync();
        outbox.Should().ContainSingle().Which.Should().Contain("userdirectory.user-deleted.v1");
    }

    [Fact]
    public async Task Export_ReturnsTheCallersIdentityData()
    {
        var user = await CreateUserAsync();
        var client = fixture.CreateClientAs(user.Id, RoleConstants.User);

        var response = await client.GetAsync($"{Profile}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("profile").GetProperty("id").GetGuid().Should().Be(user.Id);
        data.GetProperty("contact").GetProperty("email").GetString().Should().Be(user.Email);
        data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain(RoleConstants.User);
    }

    [Fact]
    public async Task Delete_ByAnAdmin_Returns409()
    {
        var user = await CreateUserAsync();
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await users.AddToRoleAsync((await users.FindByIdAsync(user.Id.ToString()))!, RoleConstants.Admin);
        }

        var client = fixture.CreateClientAs(user.Id, RoleConstants.User, RoleConstants.Admin);
        var response = await client.SendAsync(DeleteRequest(Password));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
