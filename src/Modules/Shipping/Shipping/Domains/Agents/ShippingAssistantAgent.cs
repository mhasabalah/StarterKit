using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Scripting;
using Agentic.Contracts.Tools;
using Shared.Contracts.Localization;

namespace Shipping.Domains.Agents;

/// <param name="Reference">The shipment reference the user gave, or part of it.</param>
public sealed record FindShipmentsInput(string Reference);

public sealed record FoundShipment(
    Guid Id,
    string Reference,
    string OriginCountry,
    string DestinationCountry,
    string HsCode,
    decimal DeclaredValue,
    string Currency,
    ShipmentStatus Status);

public sealed record FoundShipments(IReadOnlyList<FoundShipment> Shipments);

/// <summary>Turns the reference a person mentions into the shipment id the other tools take.</summary>
[AgentTool("shipping.find_shipments",
    "Finds shipments by their reference (or part of it), newest first, at most 10. Use it to get the id of a shipment the user names.",
    AllowAnyMember = true)]
public sealed class FindShipmentsTool(ISender sender) : IAgentTool<FindShipmentsInput, FoundShipments>
{
    public const int MaxResults = 10;

    public async Task<FoundShipments> ExecuteAsync(FindShipmentsInput input, AgentToolContext context,
        CancellationToken cancellationToken)
    {
        var page = await sender.Send(new GetShipmentsQuery { Search = input.Reference, PageNumber = 1, PageSize = MaxResults },
            cancellationToken);
        return new FoundShipments((page.Result ?? [])
            .Select(s => new FoundShipment(s.Id, s.Reference, s.OriginCountry, s.DestinationCountry, s.HsCode, s.DeclaredValue,
                s.Currency, s.Status))
            .ToList());
    }
}

/// <summary>
/// The conversational sample: answers questions about shipments over several messages ("where is REAL-1?", then "and
/// its VAT?"). Read-only by design (autonomy <see cref="AgentAutonomy.Recommend"/>, read tools only): it explains, the
/// preparation agent and people act.
/// </summary>
public static class ShippingAssistantAgent
{
    public const string Key = "shipping.assistant";

    public static readonly AgentDefinition Definition = new()
    {
        Key = Key,
        Name = new LocalizedText("Shipping assistant", "مساعد الشحن"),
        Description = new LocalizedText(
            "Answers questions about your shipments: status, route, documents, duties and VAT.",
            "يجيب عن أسئلتك حول الشحنات: الحالة والمسار والمستندات والرسوم وضريبة القيمة المضافة."),
        Instructions =
            """
            You answer questions about the user's shipments in a conversation.
            Shipment ids are never part of the conversation, and a reference is not an id. Whenever you need a shipment,
            call shipping.find_shipments with its reference first and use the id it returns, also in a follow-up question
            (take the reference from the earlier messages). Read the shipment, its duties or its documents only as the
            question needs. If a tool refuses your arguments, correct them and try again before answering.
            Answer briefly with the facts the tools returned. You cannot change anything yourself. When the user asks to
            prepare a shipment for customs clearance, find it, then hand the work to shipping.shipment-preparation with
            agent.delegate; the task must name the shipment id and its reference. A person approves the submission.
            Report what the preparation agent found. For any other change, say which action in the application does it.
            """,
        Tools = ["shipping.find_shipments", "shipping.get_shipment", "shipping.estimate_duties", "shipping.check_documents"],
        // Its own tools only read. The autonomy bounds what it may delegate: the preparation agent prepares a draft
        // and proposes the submission to a person (ADR-0033).
        Delegates = [ShipmentPreparationAgent.Key],
        Autonomy = AgentAutonomy.Execute,
        Conversational = true,
        AllowedTriggers = [AgentTriggerKind.User, AgentTriggerKind.Api],
        Limits = new AgentLimits(MaxModelTurns: 6, MaxToolCalls: 6)
    };
}

/// <summary>
/// The deterministic "model" of the assistant (scripted provider: tests, CI, demos without an API key). It finds the
/// shipment named in the message or, for a follow-up, in the earlier turns of the conversation; answers about duties
/// when asked, otherwise about the route and status.
/// </summary>
public sealed partial class ShippingAssistantScript : IAgentScript
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string AgentKey => ShippingAssistantAgent.Key;

    public ScriptedReply Next(ScriptedTurn turn)
    {
        var prompt = turn.Prompt ?? string.Empty;
        var reference = ReferenceIn(prompt)
                        ?? turn.EarlierTurns.Reverse().Select(t => ReferenceIn(t.UserMessage)).FirstOrDefault(r => r is not null);
        if (reference is null)
            return ScriptedReply.Say("Which shipment do you mean? Tell me its reference.");

        var found = Last(turn, "shipping.find_shipments");
        if (found is null)
            return Call("shipping.find_shipments", new { reference });
        var shipment = found.Value.TryGetProperty("shipments", out var list)
            ? list.EnumerateArray().FirstOrDefault(s => string.Equals(s.GetProperty("reference").GetString(), reference,
                StringComparison.OrdinalIgnoreCase))
            : default;
        if (shipment.ValueKind != JsonValueKind.Object)
            return ScriptedReply.Say($"I found no shipment with the reference {reference}.");
        var id = shipment.GetProperty("id").GetGuid();

        if (AsksToPrepare().IsMatch(prompt))
        {
            var prepared = Last(turn, AgentToolNames.Delegate);
            if (prepared is null)
            {
                return Call(AgentToolNames.Delegate, new
                {
                    agentKey = ShipmentPreparationAgent.Key,
                    task = $"Prepare shipment {id} ({reference}) for customs clearance."
                });
            }

            if (prepared.Value.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
            {
                var findings = string.Join(" ", result.GetProperty("findings").EnumerateArray().Select(f => f.GetString()));
                return ScriptedReply.Say(result.GetProperty("submitted").GetBoolean()
                    ? $"{reference} was prepared and submitted. {findings}"
                    : $"{reference} was prepared but not submitted. {findings}");
            }

            return ScriptedReply.Say(
                $"The preparation of {reference} did not finish ({prepared.Value.GetProperty("failureCode").GetString() ?? prepared.Value.GetProperty("status").GetString()}).");
        }

        if (AsksAboutDuties().IsMatch(prompt))
        {
            var estimate = Last(turn, "shipping.estimate_duties");
            if (estimate is null)
                return Call("shipping.estimate_duties", new { shipmentId = id });
            var currency = estimate.Value.GetProperty("currency").GetString();
            return ScriptedReply.Say(string.Create(CultureInfo.InvariantCulture,
                $"{reference}: duty {estimate.Value.GetProperty("duty").GetDecimal():0.00} {currency}, VAT {estimate.Value.GetProperty("vat").GetDecimal():0.00} {currency}, total {estimate.Value.GetProperty("total").GetDecimal():0.00} {currency}."));
        }

        return ScriptedReply.Say(
            $"{reference} goes from {shipment.GetProperty("originCountry").GetString()} to " +
            $"{shipment.GetProperty("destinationCountry").GetString()} (HS {shipment.GetProperty("hsCode").GetString()}); " +
            $"status: {shipment.GetProperty("status")}.");
    }

    private static JsonElement? Last(ScriptedTurn turn, string tool) =>
        turn.ToolResults.LastOrDefault(r => r.Tool == tool) is { } result
            ? JsonDocument.Parse(result.ResultJson).RootElement.Clone()
            : null;

    private static ScriptedReply Call(string tool, object arguments) =>
        ScriptedReply.Call(tool, JsonSerializer.Serialize(arguments, Json));

    private static string? ReferenceIn(string text) => ReferencePattern().Match(text) is { Success: true } match ? match.Value : null;

    [GeneratedRegex(@"\b[A-Z]{2,}-[A-Z0-9][A-Z0-9-]*\b")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"\b(vat|duty|duties|tax|taxes)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksAboutDuties();

    [GeneratedRegex(@"\b(prepare|clearance|clear)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsksToPrepare();
}
