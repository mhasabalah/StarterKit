using Identity.Contracts.Users.Queries;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Mediation;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity.IntegrationTests;

/// <summary>The admin user search treats LIKE wildcards in the search term as literal text.</summary>
[Collection(IdentityPostgresCollection.Name)]
public sealed class UserSearchTests(IdentityPostgresFixture fixture)
{
    [Fact]
    public async Task Search_WithPercentOrUnderscore_MatchesThemLiterally_NotAsWildcards()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var percent = await CreateUserAsync(firstName: $"{marker}50%off");
        var plain = await CreateUserAsync(firstName: $"{marker}500ff");
        var underscore = await CreateUserAsync(firstName: $"{marker}a_b");
        var lookalike = await CreateUserAsync(firstName: $"{marker}axb");

        (await SearchAsync($"{marker}50%")).Should().BeEquivalentTo([percent]);
        (await SearchAsync($"{marker}a_b")).Should().BeEquivalentTo([underscore]);
        (await SearchAsync(marker)).Should().BeEquivalentTo([percent, plain, underscore, lookalike]);
    }

    private async Task<Guid> CreateUserAsync(string firstName)
    {
        await using var scope = fixture.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = $"search{Guid.NewGuid():N}", Email = $"{Guid.NewGuid():N}@example.com",
            FirstName = firstName, IsActive = true
        };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, RoleConstants.User);
        return user.Id;
    }

    private async Task<IReadOnlyList<Guid>> SearchAsync(string term)
    {
        await using var scope = fixture.NewScope();
        var page = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new GetUsersQuery { SearchTerm = term, PageSize = 100 });
        return page.Result!.Select(u => u.Id).ToList();
    }
}
