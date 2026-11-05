using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.OnlinePayments.Services;

namespace Subscriptions.Domains.OnlinePayments.Features.ReceivePaymentWebhook;

/// <summary>An unauthenticated provider call: the raw body, headers and query string exactly as received.</summary>
public record ReceivePaymentWebhookCommand(
    string Provider,
    string RawBody,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string>? Query = null) : ICommand<ReceivePaymentWebhookResult>;

/// <param name="Duplicate">True when this event id was already stored (nothing is reprocessed).</param>
public record ReceivePaymentWebhookResult(bool Duplicate);

public class ReceivePaymentWebhookCommandValidator : AbstractValidator<ReceivePaymentWebhookCommand>
{
    public ReceivePaymentWebhookCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty().MaximumLength(50);
        RuleFor(x => x.RawBody).NotEmpty();
    }
}

/// <summary>
/// Verify the signature (401 / 400, nothing stored) → insert into the inbox by <c>(provider, event_id)</c> (duplicate:
/// done) → process inline (uncancellable once stored; a duplicate of an unprocessed row is processed too). A processing failure does not fail the request: the event is stored, the failure recorded,
/// and the retry job takes over; the provider must see 200 once the event is safely stored.
/// </summary>
internal sealed class ReceivePaymentWebhookHandler(
    IPaymentGatewayRegistry gateways,
    IInboundWebhookProcessor processor)
    : ICommandHandler<ReceivePaymentWebhookCommand, ReceivePaymentWebhookResult>
{
    public async Task<ReceivePaymentWebhookResult> Handle(
        ReceivePaymentWebhookCommand command,
        CancellationToken cancellationToken)
    {
        var gateway = gateways.Get(command.Provider);

        var parsed = gateway.ParseWebhook(new WebhookRequest(
            command.RawBody, command.Headers, command.Query ?? new Dictionary<string, string>()));
        if (parsed.Failure == WebhookFailure.InvalidSignature)
            throw new InvalidWebhookSignatureException();
        if (!parsed.IsValid || parsed.Event!.Amount < 0m || decimal.Round(parsed.Event.Amount, 2) != parsed.Event.Amount)
            throw new InvalidWebhookPayloadException();

        var stored = await processor.StoreAsync(gateway.Name, parsed.Event, command.RawBody, cancellationToken);

        // Once the row is stored, processing must not be abandoned because the provider's request was cancelled:
        // a Pending row nobody processes would never be settled. A duplicate whose row is not Processed (the first
        // delivery crashed or was cancelled) is processed here too; settlement is idempotent.
        if (!stored.Duplicate || stored.Status != InboundWebhookStatus.Processed)
            await processor.ProcessAsync(stored.Id, CancellationToken.None);

        return new ReceivePaymentWebhookResult(stored.Duplicate);
    }
}
