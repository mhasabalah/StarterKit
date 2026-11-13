using Shared.Core.Modularity;

namespace Agentic.Tests;

public class ModuleRegistrationTests
{
    [Fact]
    public void The_manifest_is_discoverable_and_runs_after_the_platform_modules()
    {
        var catalog = ModuleCatalog.FromAssemblies([typeof(AgenticModuleManifest).Assembly]);

        catalog.Modules.Should().ContainSingle().Which.Should().BeOfType<AgenticModuleManifest>()
            .Which.Order.Should().Be(ModuleOrder.Agentic);
        catalog.Names.Should().Equal("Agentic");
    }
}
