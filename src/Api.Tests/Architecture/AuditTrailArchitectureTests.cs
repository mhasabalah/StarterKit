using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shared.Core.Auditing;
using Shared.Core.MultiTenancy;

namespace Api.Tests.Architecture;

/// <summary>
/// RULE: a DbContext that maps <see cref="IAuditable"/> entities must map the audit table
/// (<c>builder.AddAuditTrail()</c> / <c>AddTenantAuditTrail()</c>), otherwise the interceptor can only fail at the first
/// save. A tenant-data context must use the tenant-owned row (<see cref="TenantAuditEntry"/>).
/// See wiki/Decisions/0011-audit-trail.md.
/// </summary>
public sealed class AuditTrailArchitectureTests
{
    public static TheoryData<Type> DbContexts => new(ModuleBoundaryTests.Modules
        .Select(m => Assembly.Load(m))
        .SelectMany(a => a.GetTypes())
        .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(DbContext)))
        .Where(t => t.GetConstructors().Any(c => c.GetParameters().Length == 1)));

    [Theory]
    [MemberData(nameof(DbContexts))]
    public void A_context_with_auditable_entities_maps_the_audit_table(Type contextType)
    {
        var model = BuildModel(contextType);
        var auditable = model.GetEntityTypes().Where(e => e.ClrType.IsAssignableTo(typeof(IAuditable)))
            .Select(e => e.ClrType.Name).ToList();
        if (auditable.Count == 0) return;

        var auditType = model.FindAuditEntryType();
        auditType.Should().NotBeNull($"{contextType.Name} maps IAuditable entities ({string.Join(", ", auditable)})");
        if (contextType.IsAssignableTo(typeof(ITenantDataContext)))
            auditType.Should().Be(typeof(TenantAuditEntry), "tenant-data contexts need the tenant-owned audit row");
    }

    [Fact]
    public void Catalog_audits_Product_and_Category()
    {
        var model = BuildModel(typeof(Catalog.Data.CatalogDbContext));
        model.GetEntityTypes().Where(e => e.ClrType.IsAssignableTo(typeof(IAuditable))).Select(e => e.ClrType.Name)
            .Should().Contain(["Product", "Category"]);
    }

    private static IModel BuildModel(Type contextType)
    {
        var optionsType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsType)!;
        builder.UseNpgsql("Host=localhost;Database=model_only").UseSnakeCaseNamingConvention();
        using var context = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
        return context.Model;
    }
}
