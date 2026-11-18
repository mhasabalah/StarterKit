using System.Reflection;
using Agentic.Contracts.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Architecture;

/// <summary>
/// RULE: an agent tool is an adapter over the module's own commands and queries, never a second business layer and
/// never a way around them (ADR-0025).
/// <para>
/// A tool (a class with <see cref="AgentToolAttribute"/> or implementing <see cref="IAgentTool{TInput,TOutput}"/>) is
/// what a model can ask the system to do. It must call <c>ISender</c>, so the same validation, authorization,
/// invariants, domain events and audit trail apply as for an HTTP request. It must NOT take in its constructor:
/// </para>
/// <list type="bullet">
/// <item>a <see cref="DbContext"/> or a repository (<c>I...Repository</c> / <c>...Repository</c>): direct data access
/// skips the command and with it the business rules;</item>
/// <item><see cref="IServiceProvider"/> or <see cref="IServiceScopeFactory"/>: with a service locator a tool can reach
/// anything, and nothing can be checked.</item>
/// </list>
/// The scan covers every module the host discovers (the list comes from <see cref="ModuleBoundaryTests.Modules"/>).
/// </summary>
public sealed class AgentToolArchitectureTests
{
    [Fact]
    public void Agent_tools_reach_business_data_only_through_the_mediator()
    {
        var violations = ModuleBoundaryTests.Modules
            .SelectMany(module => LoadTypes(Assembly.Load(module)))
            .Where(IsAgentTool)
            .SelectMany(tool => ForbiddenDependencies(tool).Select(reason => $"{tool.FullName}: {reason}"))
            .ToList();

        violations.Should().BeEmpty(
            "a tool must send the owning module's commands and queries through ISender; it may not read or write data " +
            "directly or resolve services by itself");
    }

    [Fact]
    public void Every_agent_tool_class_is_declared_and_typed()
    {
        var tools = ModuleBoundaryTests.Modules
            .SelectMany(module => LoadTypes(Assembly.Load(module)))
            .Where(IsAgentTool)
            .ToList();

        tools.Where(t => t.GetCustomAttribute<AgentToolAttribute>() is null).Select(t => t.FullName)
            .Should().BeEmpty("a tool needs [AgentTool(name, description)]: its name, risk and permission are its contract");
        tools.Where(t => !ImplementsToolContract(t)).Select(t => t.FullName)
            .Should().BeEmpty("[AgentTool] belongs on a class that implements IAgentTool<TInput, TOutput>");
    }

    /// <summary>The detector must flag what it is there to flag (a rule that cannot fail protects nothing).</summary>
    [Fact]
    public void The_detector_flags_direct_data_access_and_service_location()
    {
        ForbiddenDependencies(typeof(ToolWithDbContext)).Should().ContainSingle().Which.Should().Contain("DbContext");
        ForbiddenDependencies(typeof(ToolWithRepository)).Should().ContainSingle().Which.Should().Contain("repository");
        ForbiddenDependencies(typeof(ToolWithServiceProvider)).Should().HaveCount(2);
        ForbiddenDependencies(typeof(ToolWithSender)).Should().BeEmpty();
        IsAgentTool(typeof(ToolWithSender)).Should().BeTrue();
        IsAgentTool(typeof(AgentToolArchitectureTests)).Should().BeFalse();
    }

    private static bool IsAgentTool(Type type) =>
        type is { IsClass: true, IsAbstract: false } &&
        (type.GetCustomAttribute<AgentToolAttribute>() is not null || ImplementsToolContract(type));

    private static bool ImplementsToolContract(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAgentTool<,>));

    private static List<string> ForbiddenDependencies(Type tool) =>
        tool.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .Distinct()
            .Select(Forbidden)
            .OfType<string>()
            .ToList();

    private static string? Forbidden(Type dependency)
    {
        if (dependency.IsAssignableTo(typeof(DbContext)))
            return $"takes the DbContext {dependency.Name}";
        if (dependency.Name.EndsWith("Repository", StringComparison.Ordinal))
            return $"takes the repository {dependency.Name}";
        if (dependency == typeof(IServiceProvider) || dependency == typeof(IServiceScopeFactory))
            return $"takes the service locator {dependency.Name}";
        return null;
    }

    private static IEnumerable<Type> LoadTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    // ----- samples for the detector self-test -----------------------------------------------------------------------

    private sealed record Input(string Value);

    private sealed class SampleContext(DbContextOptions<SampleContext> options) : DbContext(options);

    private interface ISampleRepository;

    private abstract class SampleTool : IAgentTool<Input, Input>
    {
        public Task<Input> ExecuteAsync(Input input, AgentToolContext context, CancellationToken cancellationToken) =>
            Task.FromResult(input);
    }

#pragma warning disable CS9113 // Parameters exist only to be inspected by the detector.
    private sealed class ToolWithDbContext(SampleContext context) : SampleTool;

    private sealed class ToolWithRepository(ISampleRepository repository) : SampleTool;

    private sealed class ToolWithServiceProvider(IServiceProvider services, IServiceScopeFactory scopes) : SampleTool;

    private sealed class ToolWithSender(Shared.Contracts.Mediation.ISender sender) : SampleTool;
#pragma warning restore CS9113
}
