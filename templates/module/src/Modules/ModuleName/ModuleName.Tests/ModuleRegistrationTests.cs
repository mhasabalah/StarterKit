using Shared.Core.Modularity;

namespace ModuleName.Tests;

public class ModuleRegistrationTests
{
    [Fact]
    public void The_manifest_is_discoverable_and_runs_after_the_platform_modules()
    {
        var catalog = ModuleCatalog.FromAssemblies([typeof(ModuleNameModuleManifest).Assembly]);

        catalog.Modules.Should().ContainSingle().Which.Should().BeOfType<ModuleNameModuleManifest>()
            .Which.Order.Should().Be(ModuleOrder.Default);
        catalog.Names.Should().Equal("ModuleName");
    }
}
