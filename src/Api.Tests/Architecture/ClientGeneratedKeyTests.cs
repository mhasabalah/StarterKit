using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;

namespace Api.Tests.Architecture;

/// <summary>
/// Entities added THROUGH A PARENT'S COLLECTION (children of an aggregate, owned collections) get their Guid ids in the
/// domain (<c>Guid.NewGuid()</c> / <c>Guid.CreateVersion7()</c>). EF's convention treats a Guid key as store-generated,
/// so when DetectChanges finds a new child whose key is already set it assumes the row EXISTS and issues an UPDATE that
/// hits 0 rows: a bogus 409 "modified by another request" on the first save (found in Tenancy's
/// <c>AddTenantDomain</c>). The fix is <c>.ValueGeneratedNever()</c> on the child's key - this test enforces it for every
/// module, current and future, using the REAL models of the host.
/// </summary>
public sealed class ClientGeneratedKeyTests
{
    [Fact]
    public void Children_reached_through_a_collection_navigation_do_not_use_store_generated_guid_keys()
    {
        using var scope = RealHost.Services.CreateScope();
        using var host = TenantScope.BeginHostScope();

        var contexts = scope.ServiceProvider.GetServices<IDbContext>().OfType<DbContext>().ToList();
        contexts.Should().HaveCountGreaterThanOrEqualTo(6, "the scan must see every module context");

        var violations = contexts
            .SelectMany(context => context.Model.GetEntityTypes().Select(entity => (context, entity)))
            .Where(x => IsAddedThroughACollection(x.entity) && HasStoreGeneratedGuidKey(x.entity))
            .Select(x => $"{x.context.GetType().Name}: {x.entity.DisplayName()} (key {x.entity.FindPrimaryKey()!.Properties[0].Name})")
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, violations).Should().BeEmpty(
            "add builder.Property(x => x.Id).ValueGeneratedNever() for children whose ids are assigned in the domain");
    }

    /// <summary>Children of a collection navigation - regular (HasMany) and owned (OwnsMany) alike.</summary>
    private static bool IsAddedThroughACollection(IEntityType entity) =>
        entity.GetForeignKeys().Any(fk => fk.PrincipalToDependent is { IsCollection: true });

    private static bool HasStoreGeneratedGuidKey(IEntityType entity)
    {
        var key = entity.FindPrimaryKey();
        if (key is not { Properties.Count: 1 })
            return false;

        var property = key.Properties[0];
        return property.ClrType == typeof(Guid)
               && property.ValueGenerated != ValueGenerated.Never
               && property.GetDefaultValueSql() is null;   // a real database default would be fine
    }
}
