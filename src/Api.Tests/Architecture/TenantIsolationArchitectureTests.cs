using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;

namespace Api.Tests.Architecture;

/// <summary>
/// Guardrails that keep tenant isolation intact as the codebase grows (new modules, new entities, new queries).
/// Models are built with the Npgsql provider without a database connection.
/// </summary>
public sealed partial class TenantIsolationArchitectureTests
{
    private static readonly Assembly[] ModuleAssemblies = ModuleBoundaryTests.Modules
        .Select(m => Assembly.Load(m))
        .ToArray();

    /// <summary>Every DbContext of every module (data plane and control plane).</summary>
    public static TheoryData<Type> DbContexts => new(ModuleAssemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(DbContext)))
        .Where(t => t.GetConstructors().Any(c => c.GetParameters().Length == 1)));

    private static IEnumerable<Type> TenantDataContextTypes => ModuleAssemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(ITenantDataContext)));

    public static TheoryData<Type> TenantDataContexts => new(TenantDataContextTypes);

    [Fact]
    public void The_business_data_modules_are_tenant_data_contexts() =>
        TenantDataContextTypes.Select(t => t.Name).Should().Contain(
        [
#if (SAMPLE)
            "CatalogDbContext",
#endif
            "AttachmentDbContext", "WebhooksDbContext", "DataTransferDbContext"
        ]);

    [Theory]
    [MemberData(nameof(TenantDataContexts))]
    public void Every_entity_of_a_tenant_data_context_is_tenant_owned(Type contextType)
    {
        var offenders = RootEntities(BuildModel(contextType))
            .Where(e => !e.ClrType.IsAssignableTo(typeof(IMustHaveTenant)) &&
                        e.ClrType.GetCustomAttribute<HostEntityAttribute>() is null)
            .Select(e => e.ClrType.FullName);

        offenders.Should().BeEmpty(
            "rows of a tenant-data context must carry tenant_id (IMustHaveTenant) or be explicitly [HostEntity]");
    }

    [Theory]
    [MemberData(nameof(DbContexts))]
    public void Every_tenant_owned_entity_has_the_named_tenant_filter(Type contextType)
    {
        var offenders = RootEntities(BuildModel(contextType))
            .Where(e => e.ClrType.IsAssignableTo(typeof(IMustHaveTenant)))
            .Where(e => e.FindDeclaredQueryFilter(QueryFilterNames.Tenant) is null)
            .Select(e => e.ClrType.FullName);

        offenders.Should().BeEmpty($"{contextType.Name} must call ApplyTenantFilters(builder) at the end of OnModelCreating");
    }

    [Theory]
    [MemberData(nameof(DbContexts))]
    public void Every_query_filter_is_named(Type contextType)
    {
        var offenders = RootEntities(BuildModel(contextType))
            .SelectMany(e => e.GetDeclaredQueryFilters().Select(f => (Entity: e.ClrType.Name, f.Key)))
            .Where(f => f.Key is null)
            .Select(f => f.Entity);

        offenders.Should().BeEmpty("unnamed filters cannot be combined with the Tenant filter (use QueryFilterNames)");
    }

    /// <summary>Deliberately global unique indexes on tenant-owned entities, each with its reason.</summary>
    private static readonly Dictionary<string, string> GlobalUniqueIndexAllowList = new()
    {
        ["TenantInvitation(TokenHash)"] =
            "accepting looks the token up BEFORE the tenant is known; SHA-256 of 256 random bits cannot collide or be guessed"
    };

    [Theory]
    [MemberData(nameof(DbContexts))]
    public void Unique_indexes_of_tenant_owned_entities_include_tenant_id(Type contextType)
    {
        var offenders = RootEntities(BuildModel(contextType))
            .Where(e => e.ClrType.IsAssignableTo(typeof(IMustHaveTenant)))
            .SelectMany(e => e.GetIndexes().Where(i => i.IsUnique)
                .Where(i => i.Properties.All(p => p.Name != nameof(IMustHaveTenant.TenantId)))
                .Select(i => $"{e.ClrType.Name}({string.Join(", ", i.Properties.Select(p => p.Name))})"))
            .Where(index => !GlobalUniqueIndexAllowList.ContainsKey(index));

        offenders.Should().BeEmpty("a global unique index would let one tenant block (and detect) another tenant's values");
    }

    [Fact]
    public void Production_code_never_calls_the_parameterless_IgnoreQueryFilters()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains("Tests", StringComparison.Ordinal))
            .Where(f => ParameterlessIgnoreQueryFilters().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f));

        offenders.Should().BeEmpty(
            "IgnoreQueryFilters() also drops tenant isolation; use IgnoreNonIsolationFilters() or name the filters");
    }

    private static IModel BuildModel(Type contextType)
    {
        var optionsType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsType)!;
        builder.UseNpgsql("Host=localhost;Database=model_only").UseSnakeCaseNamingConvention();

        using var context = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
        return context.Model;
    }

    private static IEnumerable<IEntityType> RootEntities(IModel model) =>
        model.GetEntityTypes().Where(e => e.BaseType is null && !e.IsOwned() && e.ClrType != typeof(Dictionary<string, object>));

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (*.slnx) not found.");
    }

    [GeneratedRegex(@"\.IgnoreQueryFilters\(\s*\)")]
    private static partial Regex ParameterlessIgnoreQueryFilters();
}
