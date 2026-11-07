using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Tenants;
using Xunit;

namespace Api.E2ETests.MultiTenancy;

/// <summary>
/// Saved payment methods belong to the tenant subscription: only members holding <c>tenant.billing.manage</c> may
/// touch them, and an impersonated session may not change them.
/// </summary>
[Collection(MultiTenantE2ECollection.Name)]
public sealed class SavedPaymentMethodsTenantTests(MultiTenantE2EFixture fixture)
{
    private const string Api = "/api/v1";
    private const string Url = Api + "/subscriptions/saved-payment-methods";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record BranchNode(Guid Id, bool IsHeadOffice);

    private async Task<(Guid TenantId, Guid OwnerId)> CreateTenantAsync()
    {
        var ownerId = Guid.NewGuid();
        var identifier = $"spm-{Guid.NewGuid():N}"[..20];
        using var response = await fixture.Client(ownerId).PostAsJsonAsync($"{Api}/tenants", new { name = "spm", identifier });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<Envelope<CreateTenantResult>>(Json);
        return (body!.Data!.TenantId, ownerId);
    }

    [Fact]
    public async Task A_member_without_billing_manage_is_403_while_the_owner_passes()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var owner = fixture.Client(ownerId, tenant);
        var email = $"m{Guid.NewGuid():N}@example.com";
        using var tree = await owner.GetAsync($"{Api}/branches");
        var headOffice = (await tree.Content.ReadFromJsonAsync<Envelope<List<BranchNode>>>(Json))!.Data!.Single(b => b.IsHeadOffice).Id;
        using var invite = await owner.PostAsJsonAsync($"{Api}/tenant-invitations", new
        {
            email,
            roleIds = Array.Empty<Guid>(),
            branches = new[] { new { branchId = headOffice, includeDescendants = true } }
        });
        invite.StatusCode.Should().Be(HttpStatusCode.OK, await invite.Content.ReadAsStringAsync());
        var token = (await invite.Content.ReadFromJsonAsync<Envelope<InviteMemberResult>>(Json))!.Data!.Token;
        var memberId = Guid.NewGuid();
        using var accept = await fixture.Client(memberId, email: email).PostAsJsonAsync($"{Api}/tenant-invitations/accept", new { token });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());

        var member = fixture.Client(memberId, tenant);
        var id = Guid.NewGuid();

        (await member.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsync($"{Url}/{id}/default", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"{Url}/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_impersonated_session_cannot_change_or_remove_a_saved_method()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var impersonated = fixture.Client(ownerId, tenant);
        impersonated.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, Guid.NewGuid().ToString());
        var id = Guid.NewGuid();

        var setDefault = await impersonated.PostAsync($"{Url}/{id}/default", null);
        var remove = await impersonated.DeleteAsync($"{Url}/{id}");

        setDefault.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        remove.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await remove.Content.ReadAsStringAsync()).Should().Contain("impersonation");
    }
}
