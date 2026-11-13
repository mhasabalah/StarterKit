using System.Text.Json;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Tools;
using Agentic.Runtime.Tools;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Tests;

public class AgentRegistryTests
{
    [AgentTool("test.no_auth", "Declares neither a permission nor AllowAnyMember.")]
    private sealed class NoAuthorizationTool : IAgentTool<NoteInput, NoteOutput>
    {
        public Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new NoteOutput(Guid.Empty));
    }

    [AgentTool("test.open_mutation", "A mutation any member may call.", Risk = AgentToolRisk.LowRiskMutation, AllowAnyMember = true)]
    private sealed class OpenMutationTool : IAgentTool<NoteInput, NoteOutput>
    {
        public Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new NoteOutput(Guid.Empty));
    }

    [AgentTool("Test.BadName", "Upper case name.", AllowAnyMember = true)]
    private sealed class BadNameTool : IAgentTool<NoteInput, NoteOutput>
    {
        public Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new NoteOutput(Guid.Empty));
    }

    [AgentTool("test.echo", "Same name as EchoTool.", AllowAnyMember = true)]
    private sealed class DuplicateEchoTool : IAgentTool<NoteInput, NoteOutput>
    {
        public Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new NoteOutput(Guid.Empty));
    }

    private sealed class UndeclaredTool : IAgentTool<NoteInput, NoteOutput>
    {
        public Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new NoteOutput(Guid.Empty));
    }

    private static Action Building(Type tool) => () => _ = new AgentRegistry([new AgentToolRegistration(tool)], []);

    [Fact]
    public void RegisteredTools_AreDescribed_WithTheirSchemaRiskAndPermission()
    {
        var registry = TestAgents.Registry();

        var echo = registry.FindTool("test.echo")!;
        echo.Risk.Should().Be(AgentToolRisk.ReadOnly);
        echo.AllowAnyMember.Should().BeTrue();
        echo.Permission.Should().BeNull();
        echo.RequiredAutonomy.Should().Be(AgentAutonomy.Observe);
        echo.InputSchema.GetProperty("properties").TryGetProperty("text", out _).Should().BeTrue();
        echo.InputSchema.GetProperty("properties").TryGetProperty("times", out _).Should().BeTrue();

        var email = registry.FindTool("test.send_email")!;
        email.Risk.Should().Be(AgentToolRisk.ExternalOrIrreversible);
        email.Permission.Should().Be(TestPermissions.MailSend);
        email.Idempotent.Should().BeFalse();
        email.RequiredAutonomy.Should().Be(AgentAutonomy.Execute);

        registry.FindTool("test.read_document")!.ReturnsUntrustedContent.Should().BeTrue();
    }

    [Fact]
    public void ATool_WithoutAnAuthorizationDecision_StopsTheHost() =>
        Building(typeof(NoAuthorizationTool)).Should().Throw<InvalidOperationException>().WithMessage("*Permission or AllowAnyMember*");

    [Fact]
    public void AMutation_OpenToAnyMember_StopsTheHost() =>
        Building(typeof(OpenMutationTool)).Should().Throw<InvalidOperationException>().WithMessage("*needs a Permission*");

    [Fact]
    public void ABadToolName_StopsTheHost() =>
        Building(typeof(BadNameTool)).Should().Throw<InvalidOperationException>().WithMessage("*module.verb_noun*");

    [Fact]
    public void AToolClass_WithoutTheAttribute_StopsTheHost() =>
        Building(typeof(UndeclaredTool)).Should().Throw<InvalidOperationException>().WithMessage("*[AgentTool*");

    [Fact]
    public void TwoTools_WithTheSameName_StopTheHost()
    {
        var act = () => _ = new AgentRegistry(
            [new AgentToolRegistration(typeof(EchoTool)), new AgentToolRegistration(typeof(DuplicateEchoTool))], []);

        act.Should().Throw<InvalidOperationException>().WithMessage("*test.echo*");
    }

    [Fact]
    public void AnAgent_ListingAnUnregisteredTool_StopsTheHost()
    {
        var act = () => TestAgents.Registry(TestAgents.Definition(tools: ["test.echo", "test.missing"]));

        act.Should().Throw<InvalidOperationException>().WithMessage("*test.missing*");
    }

    [Fact]
    public void AnAgent_ListingAToolItsAutonomyCanNeverRun_StopsTheHost()
    {
        var act = () => TestAgents.Registry(TestAgents.Definition(autonomy: AgentAutonomy.Recommend, tools: ["test.create_note"]));

        act.Should().Throw<InvalidOperationException>().WithMessage("*could never run*");
    }

    [Fact]
    public void TwoAgents_WithTheSameKey_StopTheHost()
    {
        var act = () => TestAgents.Registry(TestAgents.Definition(), TestAgents.Definition());

        act.Should().Throw<InvalidOperationException>().WithMessage("*test.assistant*");
    }

    [Fact]
    public void ATypedAgent_GetsTheSchemaOfItsOutputType()
    {
        var registry = TestAgents.Registry(TestAgents.Definition(outputType: typeof(Summary)));

        var schema = registry.GetAgent("test.assistant").OutputSchema;

        schema.Should().NotBeNull();
        schema!.Value.GetProperty("properties").TryGetProperty("title", out _).Should().BeTrue();
        schema.Value.GetProperty("properties").TryGetProperty("items", out _).Should().BeTrue();
    }

    // ----- the typed invoker: untrusted arguments ------------------------------------------------------------------

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddAgentTool<EchoTool>();
        services.AddScoped<IValidator<EchoInput>, EchoInputValidator>();
        return services.BuildServiceProvider();
    }

    private static readonly AgentToolContext Context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "test.assistant", "agent-run:x", "key", "en");

    [Fact]
    public async Task Invoke_WithValidArguments_RunsTheTool_AndReturnsItsJson()
    {
        using var services = Services();
        var tool = TestAgents.Registry().FindTool("test.echo")!;

        var json = await tool.Invoker.InvokeAsync(services, """{"text":"ab","times":2}""", Context, CancellationToken.None);

        JsonDocument.Parse(json).RootElement.GetProperty("text").GetString().Should().Be("abab");
    }

    [Theory]
    [InlineData("""{"text":"a","tenantId":"00000000-0000-0000-0000-000000000001"}""")] // a member the input does not declare
    [InlineData("""{"text":"a","times":"many"}""")] // wrong type
    [InlineData("""["a"]""")] // not an object
    [InlineData("not json")]
    [InlineData("null")]
    public async Task Check_WithArgumentsThatDoNotMatchTheInputType_IsInvalid(string arguments)
    {
        using var services = Services();
        var tool = TestAgents.Registry().FindTool("test.echo")!;

        var check = await tool.Invoker.CheckAsync(services, arguments, CancellationToken.None);

        check.IsValid.Should().BeFalse();
        check.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Check_RunsTheRegisteredValidatorOfTheInputType()
    {
        using var services = Services();
        var tool = TestAgents.Registry().FindTool("test.echo")!;

        var check = await tool.Invoker.CheckAsync(services, """{"text":"a","times":50}""", CancellationToken.None);

        check.IsValid.Should().BeFalse();
        check.Message.Should().Contain("between 1 and 3");
    }

    [Fact]
    public async Task Invoke_WithInvalidArguments_Throws_AndNeverRunsTheTool()
    {
        using var services = Services();
        var tool = TestAgents.Registry().FindTool("test.echo")!;

        var act = () => tool.Invoker.InvokeAsync(services, """{"text":"","times":1}""", Context, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidToolArgumentsException>();
    }
}
