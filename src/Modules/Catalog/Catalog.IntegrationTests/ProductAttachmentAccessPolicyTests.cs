using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.IntegrationTests;

/// <summary>
/// Product attachment policy against real Postgres. The policy is resolved the way the Attachment module
/// resolves it (keyed by <see cref="EntityType.Product"/>), which also proves CatalogModule registers it.
/// </summary>
[Collection(CatalogPostgresCollection.Name)]
public sealed class ProductAttachmentAccessPolicyTests(CatalogPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    private static AttachmentAccessContext User(Guid id) =>
        new(id, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "User" });

    private static AttachmentAccessContext Admin() =>
        new(Guid.NewGuid(), true, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Admin" });

    private async Task<(bool Attach, bool Read)> Check(Guid productId, AttachmentAccessContext caller)
    {
        (bool, bool) result = default;
        await fx.WithScope(async sp =>
        {
            var policy = sp.GetRequiredKeyedService<IAttachmentEntityAccessPolicy>(EntityType.Product);
            result = (await policy.CanAttachAsync(productId, caller), await policy.CanReadAsync(productId, caller));
        });
        return result;
    }

    [Fact]
    public async Task Owner_can_attach_and_read_own_draft()
    {
        var id = await _scenario.Create();

        (await Check(id, User(_scenario.Owner))).Should().Be((true, true));
    }

    [Fact]
    public async Task Stranger_and_anonymous_cannot_attach_or_read_a_draft()
    {
        var id = await _scenario.Create();

        (await Check(id, User(Guid.NewGuid()))).Should().Be((false, false));
        (await Check(id, AttachmentAccessContext.Anonymous)).Should().Be((false, false));
    }

    [Fact]
    public async Task Published_product_is_readable_by_anyone_but_only_owner_can_attach()
    {
        var id = await _scenario.Published();

        (await Check(id, AttachmentAccessContext.Anonymous)).Should().Be((false, true));
        (await Check(id, User(Guid.NewGuid()))).Should().Be((false, true));
        (await Check(id, User(_scenario.Owner))).Should().Be((true, true));
    }

    [Fact]
    public async Task Admin_is_always_allowed()
    {
        var id = await _scenario.Create();

        (await Check(id, Admin())).Should().Be((true, true));
    }

    [Fact]
    public async Task Unknown_product_is_denied()
    {
        (await Check(Guid.NewGuid(), User(_scenario.Owner))).Should().Be((false, false));
    }
}
