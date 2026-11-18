using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Scripting;
using Agentic.Contracts.Tools;
using Shared.Contracts.Localization;
using Shared.Core.MultiTenancy.Authorization;

namespace Shipping.Domains.Agents;

/// <summary>The agent's typed result: what it found and what it did. Validated before the run completes.</summary>
public sealed record ShipmentPreparationResult(
    string ShipmentReference,
    IReadOnlyList<string> Findings,
    IReadOnlyList<ShipmentDocumentKind> MissingDocuments,
    decimal? EstimatedDuty,
    decimal? EstimatedVat,
    string? Currency,
    Guid? DeclarationId,
    bool Submitted,
    string? SubmissionReference);

public class ShipmentPreparationResultValidator : AbstractValidator<ShipmentPreparationResult>
{
    public ShipmentPreparationResultValidator(IStringLocalizer<ShippingResource> localizer)
    {
        RuleFor(x => x.ShipmentReference)
            .NotEmpty().WithMessage(_ => localizer["Validation.Reference.Required"])
            .MaximumLength(Shipment.ReferenceMaxLength).WithMessage(_ => localizer["Validation.Reference.MaxLength"]);
        RuleFor(x => x.Findings).Must(f => f.Count <= 20).WithMessage(_ => localizer["Validation.Result.Findings"]);
        RuleFor(x => x.SubmissionReference).NotEmpty().When(x => x.Submitted)
            .WithMessage(_ => localizer["Validation.Result.SubmissionReference"]);
    }
}

/// <summary>
/// The proof-of-concept agent: prepares a shipment for customs. It reads the shipment, estimates duties, prepares the
/// declaration draft, checks the documents, and asks for submission, which always waits for a person (external
/// action, and the run has read document text by then). Every amount and rule comes from Shipping's commands.
/// </summary>
public static class ShipmentPreparationAgent
{
    public const string Key = "shipping.shipment-preparation";

    public static readonly AgentDefinition Definition = new()
    {
        Key = Key,
        Name = new LocalizedText("Shipment preparation", "تجهيز الشحنة"),
        Description = new LocalizedText(
            "Estimates duties, prepares the customs declaration, checks the documents and submits after approval.",
            "يقدّر الرسوم ويجهّز البيان الجمركي ويتحقق من المستندات ويقدّم البيان بعد الموافقة."),
        Instructions =
            """
            You prepare one shipment for customs clearance. The shipment id is in the input.
            1. Read the shipment. 2. Estimate its duties. 3. Prepare the declaration draft (add a short note for the broker).
            4. Check the documents. If a required document is missing, do not submit: report what is missing.
            5. Otherwise submit the declaration. A person approves the submission; if they reject it, report that.
            Finish by handing in the result with your findings, the estimate, the declaration id and whether it was submitted.
            """,
        Tools =
        [
            "shipping.get_shipment", "shipping.estimate_duties", "shipping.prepare_declaration", "shipping.check_documents",
            "shipping.submit_declaration"
        ],
        Autonomy = AgentAutonomy.Execute,
        OutputType = typeof(ShipmentPreparationResult),
        RequiredPermission = TenantPermissions.ShipmentsManage,
        AllowedTriggers = [AgentTriggerKind.User, AgentTriggerKind.Api, AgentTriggerKind.Event],
        Limits = new AgentLimits(MaxModelTurns: 10, MaxToolCalls: 10)
    };

    /// <summary>The input of a run for <paramref name="shipmentId"/>.</summary>
    public static string InputFor(Guid shipmentId) => JsonSerializer.Serialize(new { shipmentId });
}

/// <summary>
/// The deterministic "model" of the agent for tests, CI and demos without an API key (scripted provider, refused in
/// Production). A pure function of the transcript: it follows the agent's instructions step by step and reacts to the
/// tool results the platform returned (errors, missing documents, a rejected approval).
/// </summary>
public sealed partial class ShipmentPreparationScript : IAgentScript
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string AgentKey => ShipmentPreparationAgent.Key;

    public ScriptedReply Next(ScriptedTurn turn)
    {
        var id = GuidPattern().Match(turn.Prompt ?? string.Empty).Value;
        if (id.Length == 0)
            return Finish("UNKNOWN", ["The input names no shipment."]);

        JsonElement? Last(string tool) => turn.ToolResults.LastOrDefault(r => r.Tool == tool) is { } result
            ? JsonDocument.Parse(result.ResultJson).RootElement.Clone()
            : null;

        var shipment = Last("shipping.get_shipment");
        if (shipment is null)
            return Call("shipping.get_shipment", new { shipmentId = id });
        if (Error(shipment.Value) is { } shipmentError)
            return Finish("UNKNOWN", [$"The shipment could not be read ({shipmentError})."]);
        var reference = shipment.Value.GetProperty("reference").GetString()!;

        var estimate = Last("shipping.estimate_duties");
        if (estimate is null)
            return Call("shipping.estimate_duties", new { shipmentId = id });

        var draft = Last("shipping.prepare_declaration");
        if (draft is null)
            return Call("shipping.prepare_declaration", new { shipmentId = id, notes = "Prepared by the shipment preparation agent." });
        if (Error(draft.Value) is { } draftError)
            return Finish(reference, [$"The declaration could not be prepared ({draftError})."], estimate);

        var documents = Last("shipping.check_documents");
        if (documents is null)
            return Call("shipping.check_documents", new { shipmentId = id });
        var missing = documents.Value.TryGetProperty("missing", out var list)
            ? list.EnumerateArray().Select(m => Enum.Parse<ShipmentDocumentKind>(m.GetString()!)).ToList()
            : [];
        var declarationId = draft.Value.GetProperty("id").GetGuid();
        if (missing.Count > 0)
        {
            return Finish(reference, [$"Not submitted: missing {string.Join(", ", missing)}."], estimate, declarationId,
                missing: missing);
        }

        var submitted = Last("shipping.submit_declaration");
        if (submitted is null)
            return Call("shipping.submit_declaration", new { declarationId });
        if (Error(submitted.Value) is { } submitError)
            return Finish(reference, [$"Not submitted ({submitError})."], estimate, declarationId);

        return Finish(reference, ["Duties estimated, documents complete, declaration submitted."], estimate, declarationId,
            submitted.Value.GetProperty("submissionReference").GetString());
    }

    private static ScriptedReply Call(string tool, object arguments) =>
        ScriptedReply.Call(tool, JsonSerializer.Serialize(arguments, Json));

    private static string? Error(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object && result.TryGetProperty("error", out var error)
            ? error.GetProperty("code").GetString()
            : null;

    private static ScriptedReply Finish(string reference, IReadOnlyList<string> findings, JsonElement? estimate = null,
        Guid? declarationId = null, string? submissionReference = null, IReadOnlyList<ShipmentDocumentKind>? missing = null)
    {
        var result = new ShipmentPreparationResult(
            reference,
            findings,
            missing ?? [],
            estimate?.GetProperty("duty").GetDecimal(),
            estimate?.GetProperty("vat").GetDecimal(),
            estimate?.GetProperty("currency").GetString(),
            declarationId,
            submissionReference is not null,
            submissionReference);
        return ScriptedReply.Submit(JsonSerializer.Serialize(result, Json));
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();
}
