using Catalog.Contracts.Products.IntegrationEvents;
using Catalog.Domains.Products.Consumers;
using FluentAssertions;
using Xunit;

namespace Catalog.Tests;

public class ProductPublishedInAppTests
{
    [Fact]
    public void The_owner_gets_a_catalog_inbox_entry_in_both_languages()
    {
        var content = ProductPublishedConsumer.Content(new ProductPublishedIntegrationEvent
        {
            ProductId = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Sku = "SKU-1", PublishedAt = DateTime.UtcNow
        });

        content.Category.Should().Be("catalog.product-published");
        content.Body.En.Should().Contain("SKU-1");
        content.Body.Ar.Should().Contain("SKU-1").And.NotBe(content.Body.En);
        content.Title.Ar.Should().NotBe(content.Title.En);
    }
}
