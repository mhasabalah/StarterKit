using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Runtime.Tools;

/// <summary>JSON rules for everything that crosses the model boundary (tool arguments, tool results, typed results).</summary>
public static class AgentJson
{
    /// <summary>
    /// Model output is untrusted: property names are matched case-insensitively (camelCase on the wire), but a member
    /// the type does not declare is an ERROR, never silently dropped, and so is a missing required one. Enums travel
    /// as their names.
    /// </summary>
    public static readonly JsonSerializerOptions Strict = Create(JsonUnmappedMemberHandling.Disallow, strict: true);

    /// <summary>Results we produce ourselves.</summary>
    public static readonly JsonSerializerOptions Output = Create(JsonUnmappedMemberHandling.Skip, strict: false);

    private static JsonSerializerOptions Create(JsonUnmappedMemberHandling unmapped, bool strict)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = unmapped,
            // A constructor parameter without a default is REQUIRED, and null is refused where the type says non-null:
            // a field the model left out never silently becomes null or zero.
            RespectRequiredConstructorParameters = strict,
            RespectNullableAnnotations = strict,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            MaxDepth = 32
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <summary><c>{"error":{"code":"...","message":"..."}}</c>: how a refused or failed call is reported to the model.</summary>
    public static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { error = new { code, message } }, Output);

    public static bool IsJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>The outcome of parsing and validating untrusted arguments.</summary>
public readonly record struct ToolInputCheck(bool IsValid, string? Message)
{
    public static readonly ToolInputCheck Valid = new(true, null);

    public static ToolInputCheck Invalid(string message) => new(false, message);
}

/// <summary>
/// Calls one registered tool with its real types. Built once per tool when the registry starts (a closed generic over
/// the tool's own input and output types), so a call is an ordinary typed interface call: no name-based dispatch and
/// nothing the model writes can choose a method.
/// </summary>
public interface IAgentToolInvoker
{
    /// <summary>Strict parse + the registered validators of the input type. Runs nothing.</summary>
    Task<ToolInputCheck> CheckAsync(IServiceProvider services, string argumentsJson, CancellationToken cancellationToken);

    /// <summary>Parses, validates and runs the tool; returns its output as JSON.</summary>
    Task<string> InvokeAsync(IServiceProvider services, string argumentsJson, AgentToolContext context,
        CancellationToken cancellationToken);
}

/// <summary>Thrown by <see cref="IAgentToolInvoker.InvokeAsync"/> when the arguments do not pass <c>CheckAsync</c>.</summary>
public sealed class InvalidToolArgumentsException(string message) : Exception(message);

internal sealed class AgentToolInvoker<TTool, TInput, TOutput> : IAgentToolInvoker
    where TTool : class, IAgentTool<TInput, TOutput>
    where TInput : class
{
    public async Task<ToolInputCheck> CheckAsync(IServiceProvider services, string argumentsJson,
        CancellationToken cancellationToken)
    {
        var (input, error) = Parse(argumentsJson);
        if (input is null)
            return ToolInputCheck.Invalid(error!);
        return await ValidateAsync(services, input, cancellationToken) is { } message
            ? ToolInputCheck.Invalid(message)
            : ToolInputCheck.Valid;
    }

    public async Task<string> InvokeAsync(IServiceProvider services, string argumentsJson, AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var (input, error) = Parse(argumentsJson);
        if (input is null)
            throw new InvalidToolArgumentsException(error!);
        if (await ValidateAsync(services, input, cancellationToken) is { } message)
            throw new InvalidToolArgumentsException(message);

        var tool = services.GetRequiredService<TTool>();
        var output = await tool.ExecuteAsync(input, context, cancellationToken);
        return JsonSerializer.Serialize(output, AgentJson.Output);
    }

    private static (TInput? Input, string? Error) Parse(string argumentsJson)
    {
        try
        {
            var json = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson;
            var input = JsonSerializer.Deserialize<TInput>(json, AgentJson.Strict);
            return input is null ? (null, AgenticText.Get("Agent.Tool.arguments_not_object")) : (input, null);
        }
        catch (JsonException ex)
        {
            // The serializer's message names the offending member or path; it never contains values of other fields.
            return (null, ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return (null, ex.Message);
        }
    }

    private static async Task<string?> ValidateAsync(IServiceProvider services, TInput input, CancellationToken cancellationToken)
    {
        foreach (var validator in services.GetServices<IValidator<TInput>>())
        {
            var result = await validator.ValidateAsync(input, cancellationToken);
            if (!result.IsValid)
                return string.Join(" ", result.Errors.Select(e => e.ErrorMessage));
        }

        return null;
    }
}

/// <summary>Everything the runtime knows about a registered tool (read once from its <see cref="AgentToolAttribute"/>).</summary>
public sealed record AgentToolDescriptor(
    string Name,
    int Version,
    string Description,
    AgentToolRisk Risk,
    string? Permission,
    bool AllowAnyMember,
    TimeSpan Timeout,
    bool Idempotent,
    bool ReturnsUntrustedContent,
    Type ToolType,
    Type InputType,
    Type OutputType,
    JsonElement InputSchema,
    IAgentToolInvoker Invoker)
{
    /// <summary>The autonomy level a run needs before this tool may run at all.</summary>
    public AgentAutonomy RequiredAutonomy => Risk switch
    {
        AgentToolRisk.ReadOnly => AgentAutonomy.Observe,
        AgentToolRisk.LowRiskMutation => AgentAutonomy.Prepare,
        _ => AgentAutonomy.Execute
    };

    public bool IsMutation => Risk != AgentToolRisk.ReadOnly;
}
