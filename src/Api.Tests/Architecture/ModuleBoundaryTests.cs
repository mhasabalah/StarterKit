using System.Reflection;
using Shared.Core.Modularity;

namespace Api.Tests.Architecture;

/// <summary>
/// Enforces the modular-monolith dependency rules from CLAUDE.md at the assembly level:
/// <list type="bullet">
/// <item>A module talks to another module ONLY through that module's <c>.Contracts</c> assembly (ISender + contracts).</item>
/// <item><c>.Contracts</c> assemblies never depend on module implementations.</item>
/// <item>Core shared kernel (Shared.Contracts / Shared.Core / Shared.Messaging) never depends on any module.</item>
/// <item>Identity is reached ONLY through the provider-neutral user-directory port (<c>UserDirectory.Contracts</c>);
/// <c>Identity.Contracts</c> (auth/admin workflows) is private to Identity and the host, so the identity provider
/// can be replaced (e.g. by a Keycloak adapter) without touching other modules.</item>
/// </list>
/// The module list is the host's real one (<see cref="ModuleCatalog"/>, ADR-0019): a new module is covered as soon as
/// its folder exists.
/// </summary>
public sealed class ModuleBoundaryTests
{
    /// <summary>
    /// Business modules the host discovers (src/Modules), in startup order. Each has &lt;Name&gt; and
    /// &lt;Name&gt;.Contracts assemblies.
    /// </summary>
    public static readonly string[] Modules = [.. ModuleCatalog.FromHost(typeof(Program).Assembly).Names];

    /// <summary>Shared kernel that must stay module-agnostic.</summary>
    private static readonly string[] CoreShared = ["Shared.Contracts", "Shared.Core", "Shared.Messaging"];

    /// <summary>
    /// Provider-neutral ports implemented by a module but owned by nobody: they may depend on the shared kernel only.
    /// </summary>
    public static readonly string[] Ports = ["UserDirectory.Contracts"];

    public static TheoryData<string> ModuleNames => new(Modules);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_references_other_modules_only_through_contracts(string module)
    {
        var references = ReferencedNames(module);

        var violations = Modules
            .Where(other => other != module)
            .Where(references.Contains)
            .ToList();

        violations.Should().BeEmpty(
            $"{module} must call other modules via their .Contracts (ISender), not their implementation assemblies");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Contracts_never_reference_module_implementations(string module)
    {
        var references = ReferencedNames($"{module}.Contracts");

        references.Intersect(Modules).Should().BeEmpty(
            $"{module}.Contracts is shared with other modules and must stay implementation-free");
    }

    [Theory]
    [MemberData(nameof(CoreSharedNames))]
    public void Core_shared_kernel_never_references_modules(string sharedAssembly)
    {
        var references = ReferencedNames(sharedAssembly);
        var moduleAssemblies = Modules.SelectMany(m => new[] { m, $"{m}.Contracts" });

        references.Intersect(moduleAssemblies).Should().BeEmpty(
            $"{sharedAssembly} is the shared kernel; modules depend on it, never the other way round");
    }

    [Theory]
    [MemberData(nameof(PortNames))]
    public void Ports_depend_on_the_shared_kernel_only(string port)
    {
        var references = ReferencedNames(port);
        var moduleAssemblies = Modules.SelectMany(m => new[] { m, $"{m}.Contracts" });

        references.Intersect(moduleAssemblies).Should().BeEmpty(
            $"{port} is a provider-neutral port; it must not depend on any module or module contracts");
    }

    [Theory]
    [MemberData(nameof(NonIdentityAssemblyNames))]
    public void Only_Identity_references_Identity_Contracts(string assembly)
    {
        ReferencedNames(assembly).Should().NotContain("Identity.Contracts",
            $"{assembly} must reach users through UserDirectory.Contracts so the identity provider stays replaceable");
    }

    public static TheoryData<string> PortNames => new(Ports);

    public static TheoryData<string> NonIdentityAssemblyNames => new(
        Modules.Where(m => m != "Identity")
            .SelectMany(m => new[] { m, $"{m}.Contracts" })
            .Concat(CoreShared));

    public static TheoryData<string> CoreSharedNames => new(CoreShared);

    private static HashSet<string> ReferencedNames(string assemblyName) =>
        Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToHashSet(StringComparer.Ordinal);
}
