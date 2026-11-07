using System.Reflection;
using Shared.Core.Modularity;
using Shared.Core.MultiTenancy;

namespace Api.Tests.Architecture;

/// <summary>
/// Guards module discovery (ADR-0019): the host's module list is the src/Modules folder set, every module declares
/// exactly one manifest, and the declared <see cref="ModuleOrder"/> keeps the startup constraints.
/// </summary>
public sealed class ModuleDiscoveryTests
{
    private static readonly ModuleCatalog Catalog = ModuleCatalog.FromHost(typeof(Program).Assembly);

    [Fact]
    public void Every_module_folder_is_discovered_by_the_host()
    {
        var modulesDir = Path.Combine(FindRepositoryRoot(), "src", "Modules");
        var folders = Directory.EnumerateDirectories(modulesDir)
            .Select(Path.GetFileName)
            .Where(m => File.Exists(Path.Combine(modulesDir, m!, m!, $"{m}.csproj")))
            .ToList();

        Catalog.Names.Should().BeEquivalentTo(folders,
            "every src/Modules/<M>/<M>/<M>.csproj is a module the host must reference (Api.csproj glob) and discover");
    }

    [Fact]
    public void Every_module_declares_exactly_one_manifest_named_after_it()
    {
        var violations = new List<string>();
        foreach (var module in Catalog.Modules)
        {
            var assembly = module.GetType().Assembly;
            var manifests = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IModule)))
                .ToList();

            if (manifests.Count != 1)
            {
                violations.Add($"{assembly.GetName().Name}: {manifests.Count} IModule types (expected 1)");
            }

            if (module.Name != assembly.GetName().Name)
            {
                violations.Add($"{module.GetType().FullName}: Name '{module.Name}' != assembly '{assembly.GetName().Name}'");
            }

            if (module.GetType().Name != $"{module.Name}ModuleManifest")
            {
                violations.Add($"{module.GetType().FullName}: expected the name {module.Name}ModuleManifest");
            }
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Identity_starts_first_and_Notification_last()
    {
        Catalog.Names[0].Should().Be("Identity", "every module builds on the auth services");
        Catalog.Names[^1].Should().Be("Notification", "it may override other modules' defaults and maps the hub");
    }

    [Fact]
    public void Tenancy_starts_before_every_tenant_data_module_and_Subscriptions()
    {
        var names = Catalog.Names.ToList();
        var tenancy = names.IndexOf("Tenancy");

        var tooEarly = Catalog.Modules
            .Where(m => m.Name == "Subscriptions" || m.GetType().Assembly.GetTypes()
                .Any(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(ITenantDataContext))))
            .Where(m => names.IndexOf(m.Name) < tenancy)
            .Select(m => m.Name);

        tooEarly.Should().BeEmpty(
            "the Default tenant must exist before tenant-data seeding, and Tenancy overrides the subscriber defaults");
    }

    [Fact]
    public void Webhooks_starts_after_the_modules_whose_public_events_it_forwards()
    {
        var names = Catalog.Names.ToList();
        var webhooks = names.IndexOf("Webhooks");

        names.Where(n => n is "Catalog" or "Subscriptions")
            .Should().OnlyContain(n => names.IndexOf(n) < webhooks);
    }

    [Fact]
    public void A_module_assembly_without_a_manifest_fails_fast()
    {
        var act = () => ModuleCatalog.FromAssemblies([typeof(ModuleDiscoveryTests).Assembly]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*declares no manifest*");
    }

    [Fact]
    public void Consumer_and_enum_assemblies_come_from_the_manifests()
    {
        Catalog.ConsumerAssemblies.Select(a => a.GetName().Name).Should().Contain(Catalog.Names);
        Catalog.EnumAssemblies.Should().OnlyHaveUniqueItems();
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any())
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (*.slnx) not found.");
    }
}
