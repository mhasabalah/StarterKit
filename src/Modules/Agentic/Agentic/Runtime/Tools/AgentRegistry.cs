using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Agentic.Runtime.Tools;

/// <summary>A registered agent with what the runtime derived from its definition.</summary>
/// <param name="Tools">The agent's allowlist, resolved.</param>
/// <param name="OutputSchema">JSON schema of the typed result, or null for a free-text agent.</param>
public sealed record RegisteredAgent(
    AgentDefinition Definition,
    IReadOnlyList<AgentToolDescriptor> Tools,
    JsonElement? OutputSchema)
{
    public bool Allows(string toolName) => Tools.Any(t => t.Name == toolName);
}

/// <summary>
/// The agents and tools the modules registered (<c>AddAgent</c> / <c>AddAgentTool</c>). Built once and validated at
/// startup: a wiring mistake (duplicate name, a tool without an authorization decision, an agent that lists an unknown
/// tool) stops the host instead of surfacing in a customer's run.
/// </summary>
public sealed partial class AgentRegistry
{
    private readonly Dictionary<string, AgentToolDescriptor> _tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RegisteredAgent> _agents = new(StringComparer.Ordinal);

    public AgentRegistry(IEnumerable<AgentToolRegistration> tools, IEnumerable<AgentDefinition> agents)
    {
        foreach (var registration in tools)
        {
            var descriptor = Describe(registration.ToolType);
            if (!_tools.TryAdd(descriptor.Name, descriptor))
            {
                throw new InvalidOperationException(
                    $"Two agent tools use the name '{descriptor.Name}' ({_tools[descriptor.Name].ToolType.FullName} and " +
                    $"{registration.ToolType.FullName}).");
            }
        }

        foreach (var definition in agents)
        {
            var agent = Describe(definition);
            if (!_agents.TryAdd(definition.Key, agent))
                throw new InvalidOperationException($"Two agents use the key '{definition.Key}'.");
        }
    }

    public IReadOnlyCollection<RegisteredAgent> Agents => _agents.Values;

    public IReadOnlyCollection<AgentToolDescriptor> Tools => _tools.Values;

    public RegisteredAgent? FindAgent(string key) => _agents.GetValueOrDefault(key);

    public RegisteredAgent GetAgent(string key) => FindAgent(key) ?? throw new AgentNotFoundException(key);

    public AgentToolDescriptor? FindTool(string name) => _tools.GetValueOrDefault(name);

    // ----- tools ------------------------------------------------------------------------------------------------

    private static AgentToolDescriptor Describe(Type toolType)
    {
        var attribute = toolType.GetCustomAttributes(typeof(AgentToolAttribute), inherit: false)
                            .Cast<AgentToolAttribute>().SingleOrDefault()
                        ?? throw new InvalidOperationException(
                            $"Agent tool {toolType.FullName} must carry [AgentTool(name, description)].");

        var contracts = toolType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAgentTool<,>))
            .ToList();
        if (contracts.Count != 1)
        {
            throw new InvalidOperationException(
                $"Agent tool {toolType.FullName} must implement IAgentTool<TInput, TOutput> exactly once.");
        }

        if (!ToolNameRegex().IsMatch(attribute.Name) || attribute.Name.Contains("__", StringComparison.Ordinal) ||
            attribute.Name.Length > AgentRunStep.ToolNameMaxLength)
        {
            throw new InvalidOperationException(
                $"Agent tool name '{attribute.Name}' ({toolType.FullName}) must look like 'module.verb_noun' " +
                "(lower case letters, digits and single underscores).");
        }

        if (attribute.Name == AgentToolNames.SubmitResult)
            throw new InvalidOperationException($"'{AgentToolNames.SubmitResult}' is reserved ({toolType.FullName}).");
        if (string.IsNullOrWhiteSpace(attribute.Description))
            throw new InvalidOperationException($"Agent tool '{attribute.Name}' needs a description for the model.");
        if (attribute.TimeoutSeconds < 1)
            throw new InvalidOperationException($"Agent tool '{attribute.Name}': TimeoutSeconds must be at least 1.");

        // Explicit authorization, like endpoints: a permission, or a stated "any member".
        var hasPermission = !string.IsNullOrWhiteSpace(attribute.Permission);
        if (hasPermission == attribute.AllowAnyMember)
        {
            throw new InvalidOperationException(
                $"Agent tool '{attribute.Name}' must declare exactly one of Permission or AllowAnyMember = true.");
        }

        if (attribute.AllowAnyMember && attribute.Risk != AgentToolRisk.ReadOnly)
        {
            throw new InvalidOperationException(
                $"Agent tool '{attribute.Name}' changes data, so it needs a Permission (AllowAnyMember is for read-only tools).");
        }

        var arguments = contracts[0].GetGenericArguments();
        var (inputType, outputType) = (arguments[0], arguments[1]);
        var invoker = (IAgentToolInvoker)Activator.CreateInstance(
            typeof(AgentToolInvoker<,,>).MakeGenericType(toolType, inputType, outputType))!;

        return new AgentToolDescriptor(
            attribute.Name,
            attribute.Version,
            attribute.Description.Trim(),
            attribute.Risk,
            hasPermission ? attribute.Permission : null,
            attribute.AllowAnyMember,
            TimeSpan.FromSeconds(attribute.TimeoutSeconds),
            attribute.Idempotent,
            attribute.ReturnsUntrustedContent,
            attribute.RequiresApproval,
            toolType,
            inputType,
            outputType,
            SchemaOf(inputType),
            invoker);
    }

    /// <summary>JSON schema of a tool input or a typed result, as sent to the model.</summary>
    public static JsonElement SchemaOf(Type type) =>
        AIJsonUtilities.CreateJsonSchema(type, serializerOptions: AgentJson.Strict);

    // ----- agents -----------------------------------------------------------------------------------------------

    private RegisteredAgent Describe(AgentDefinition definition)
    {
        if (!AgentKeyRegex().IsMatch(definition.Key) || definition.Key.Length > AgentRun.AgentKeyMaxLength)
        {
            throw new InvalidOperationException(
                $"Agent key '{definition.Key}' must look like 'module.agent-name' (lower case letters, digits and hyphens).");
        }

        if (string.IsNullOrWhiteSpace(definition.Instructions))
            throw new InvalidOperationException($"Agent '{definition.Key}' needs instructions.");
        if (definition.Version < 1 || definition.MinResumableVersion < 1 || definition.MinResumableVersion > definition.Version)
        {
            throw new InvalidOperationException(
                $"Agent '{definition.Key}': Version and MinResumableVersion must be positive, and MinResumableVersion <= Version.");
        }

        if (definition.AllowedTriggers.Count == 0)
            throw new InvalidOperationException($"Agent '{definition.Key}' allows no trigger at all.");
        if (definition.Tools.Distinct(StringComparer.Ordinal).Count() != definition.Tools.Count)
            throw new InvalidOperationException($"Agent '{definition.Key}' lists a tool twice.");

        var tools = new List<AgentToolDescriptor>(definition.Tools.Count);
        foreach (var name in definition.Tools)
        {
            var tool = FindTool(name) ?? throw new InvalidOperationException(
                $"Agent '{definition.Key}' lists the tool '{name}', which no module registered (AddAgentTool).");
            if (tool.RequiredAutonomy > definition.Autonomy)
            {
                throw new InvalidOperationException(
                    $"Agent '{definition.Key}' ({definition.Autonomy}) lists the tool '{name}', which needs " +
                    $"{tool.RequiredAutonomy}: it could never run. Raise the agent's autonomy or drop the tool.");
            }

            tools.Add(tool);
        }

        JsonElement? outputSchema = definition.OutputType is { } output ? SchemaOf(output) : null;
        return new RegisteredAgent(definition, tools, outputSchema);
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex ToolNameRegex();

    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9-]*)+$")]
    private static partial Regex AgentKeyRegex();
}
