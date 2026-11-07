using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Models;
using Shared.Contracts.Mediation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using UserDirectory.Contracts.Queries;

namespace Identity.IntegrationTests;

/// <summary>Identity's implementation of the provider-neutral user-directory port, against real PostgreSQL.</summary>
[Collection(IdentityPostgresCollection.Name)]
public sealed class UserDirectoryHandlerTests(IdentityPostgresFixture fixture)
{
    private async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request)
    {
        await using var scope = fixture.NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private async Task UpdateAsync(Guid userId, Action<ApplicationUser> change)
    {
        await using var scope = fixture.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId.ToString()))!;
        change(user);
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Profile_of_unknown_user_is_null()
    {
        (await SendAsync(new GetUserProfileQuery(Guid.NewGuid()))).Should().BeNull();
    }

    [Fact]
    public async Task Profile_carries_display_and_contact_data()
    {
        var user = await fixture.CreateUserAsync(phone: "+201000000001");
        await UpdateAsync(user.Id, u => { u.FirstName = "Jane"; u.LastName = "Doe"; });

        var profile = await SendAsync(new GetUserProfileQuery(user.Id));

        profile.Should().Be(new UserProfileDto(user.Id, "Jane Doe", user.Email, "+201000000001", true));
    }

    [Fact]
    public async Task Profile_display_name_falls_back_to_email()
    {
        var user = await fixture.CreateUserAsync();

        var profile = await SendAsync(new GetUserProfileQuery(user.Id));

        profile!.DisplayName.Should().Be(user.Email);
    }

    [Fact]
    public async Task Users_in_role_returns_only_active_holders_of_that_role()
    {
        var active = await fixture.CreateUserAsync(null, RoleConstants.Support);
        var inactive = await fixture.CreateUserAsync(null, RoleConstants.Support);
        await UpdateAsync(inactive.Id, u => u.IsActive = false);
        var otherRole = await fixture.CreateUserAsync(null, RoleConstants.User);

        var users = await SendAsync(new GetUsersInRoleQuery(RoleConstants.Support, GetUsersInRoleQuery.MaxLimit));

        var ids = users.Select(u => u.UserId).ToList();
        ids.Should().Contain(active.Id);
        ids.Should().NotContain([inactive.Id, otherRole.Id]);
        users.Should().OnlyContain(u => u.IsActive);
        users.Single(u => u.UserId == active.Id).Email.Should().Be(active.Email);
    }

    [Fact]
    public async Task Users_in_role_honours_the_limit()
    {
        await fixture.CreateUserAsync(null, RoleConstants.Finance);
        await fixture.CreateUserAsync(null, RoleConstants.Finance);

        var users = await SendAsync(new GetUsersInRoleQuery(RoleConstants.Finance, 1));

        users.Should().ContainSingle();
    }

    [Fact]
    public async Task Users_in_unknown_role_is_empty()
    {
        (await SendAsync(new GetUsersInRoleQuery("no-such-role"))).Should().BeEmpty();
    }

    [Fact]
    public async Task User_ids_by_emails_matches_case_insensitively_and_omits_unknown_addresses()
    {
        var user = await fixture.CreateUserAsync();
        var other = await fixture.CreateUserAsync();

        var map = await SendAsync(new GetUserIdsByEmailsQuery(
            [user.Email!.ToUpperInvariant(), "nobody-" + Guid.NewGuid().ToString("N") + "@example.com", " "]));

        map.Should().ContainSingle();
        map[user.Email.ToLowerInvariant()].Should().Be(user.Id);
        map.ContainsKey(other.Email!).Should().BeFalse();
        (await SendAsync(new GetUserIdsByEmailsQuery([]))).Should().BeEmpty();
    }

    [Fact]
    public async Task User_ids_by_emails_refuses_more_than_the_maximum()
    {
        var act = () => SendAsync(new GetUserIdsByEmailsQuery(
            Enumerable.Range(0, GetUserIdsByEmailsQuery.MaxEmails + 1).Select(i => $"u{i}@example.com").ToList()));

        await act.Should().ThrowAsync<Shared.Core.Exceptions.BadRequestException>();
    }

    [Fact]
    public async Task User_roles_lists_the_role_names_and_is_empty_for_unknown_users()
    {
        var admin = await fixture.CreateUserAsync(null, RoleConstants.Admin);

        (await SendAsync(new GetUserRolesQuery(admin.Id))).Should().Contain(RoleConstants.Admin);
        (await SendAsync(new GetUserRolesQuery(Guid.NewGuid()))).Should().BeEmpty();
    }
}
