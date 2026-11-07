using Catalog.Domains.Categories.Cache;
using Catalog.Domains.Categories.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.MultiTenancy;

namespace Catalog.Tests.Services;

/// <summary>A bulk import raises one change event per category; the scoped cache removes the key once per write cycle.</summary>
public sealed class CategoryTreeCacheInvalidationTests
{
    private readonly Mock<IDistributedCache> _distributed = new();

    private CategoryTreeCache Sut()
    {
        var keys = new Mock<ITenantCacheKeys>();
        keys.Setup(k => k.For(It.IsAny<string>())).Returns<string>(k => "t:" + k);
        return new CategoryTreeCache(_distributed.Object, keys.Object, NullLogger<CategoryTreeCache>.Instance);
    }

    [Fact]
    public async Task Many_invalidations_in_one_scope_remove_the_key_once()
    {
        var cache = Sut();

        for (var i = 0; i < 50; i++)
            await cache.InvalidateAsync();

        _distributed.Verify(d => d.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task After_the_tree_is_written_again_the_next_change_invalidates_again()
    {
        var cache = Sut();

        await cache.InvalidateAsync();
        await cache.SetAsync([new CategoryNodeSnapshot(Guid.NewGuid(), null, "a", "a", "a", 0)]);
        await cache.InvalidateAsync();

        _distributed.Verify(d => d.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
