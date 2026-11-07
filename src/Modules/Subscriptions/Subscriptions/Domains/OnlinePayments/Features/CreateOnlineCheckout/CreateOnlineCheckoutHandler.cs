using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Data.Repositories;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.Subscriptions.Services;
using Microsoft.Extensions.Localization;
using Subscriptions.Options;
using Subscriptions.Resources;

namespace Subscriptions.Domains.OnlinePayments.Features.CreateOnlineCheckout;

public class CreateOnlineCheckoutCommandValidator : AbstractValidator<CreateOnlineCheckoutCommand>
{
    private readonly HashSet<string> _allowedOrigins;

    public CreateOnlineCheckoutCommandValidator(
        IOptions<SubscriptionOptions> options,
        IConfiguration configuration,
        IStringLocalizer<SubscriptionsResource> localizer)
    {
        // Return URLs must point at our own front ends: otherwise the checkout is an open redirect.
        var origins = options.Value.OnlinePaymentReturnOrigins.Length > 0
            ? options.Value.OnlinePaymentReturnOrigins
            : configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        _allowedOrigins = origins.Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim().TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.Provider).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SuccessUrl).Must(BeAllowedReturnUrl)
            .WithMessage(_ => localizer["Validation.SuccessUrl.Invalid"]);
        RuleFor(x => x.CancelUrl).Must(BeAllowedReturnUrl)
            .WithMessage(_ => localizer["Validation.CancelUrl.Invalid"]);
    }

    private bool BeAllowedReturnUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
        (_allowedOrigins.Count == 0 || _allowedOrigins.Contains(uri.GetLeftPart(UriPartial.Authority)));
}

/// <summary>
/// Creates a hosted checkout for the payment the subscription owes and records it as an
/// <see cref="OnlinePaymentSession"/> so the provider's webhook can find the payment. Nothing is settled here.
/// </summary>
internal sealed class CreateOnlineCheckoutHandler(
    ISubscriptionRepository subscriptionRepository,
    IPaymentGatewayRegistry gateways,
    IOnlinePaymentSessionRepository sessions,
    ISavedPaymentMethodRepository savedMethods,
    IOptions<SubscriptionOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<CreateOnlineCheckoutCommand, CreateOnlineCheckoutResult>
{
    public async Task<CreateOnlineCheckoutResult> Handle(
        CreateOnlineCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        var gateway = gateways.Get(command.Provider);

        // Another subscriber's subscription is reported as missing, like the proof upload does.
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken);
        if (subscription is null || subscription.SubscriberId != command.SubscriberId)
            throw new SubscriptionNotFoundException(command.SubscriptionId);

        var payment = await PaymentSelection.ResolveOwedAsync(
            subscriptionRepository, subscription, command.PaymentId, cancellationToken);

        var amount = payment.OutstandingAmount;
        if (amount <= 0m)
            throw new ConflictException("Nothing is outstanding on this payment.");

        // Keep the card only when the subscriber opted in, automatic retries are on and the provider can charge it later; reuse the
        // provider customer the subscriber already has so cards do not pile up under duplicate customers.
        var savePaymentMethod = command.SaveCard && options.Value.Dunning.AutoRetry.Enabled && gateway.SupportsOffSessionCharges;
        var customerReference = savePaymentMethod
            ? (await savedMethods.GetActiveAsync(subscription.SubscriberId, cancellationToken))
                .FirstOrDefault(x => string.Equals(x.Gateway, gateway.Name, StringComparison.OrdinalIgnoreCase))?.ProviderCustomerRef
            : null;

        if (!gateway.SupportsAmount(amount, payment.Currency))
        {
            throw new ConflictException(
                $"{gateway.Name} cannot charge {CurrencyRules.FormatWithCode(amount, payment.Currency)}; pay with another method.")
            {
                ErrorCode = "amount_not_supported"
            };
        }

        var session = await gateway.CreateCheckoutAsync(
            new CheckoutRequest(
                payment.Id,
                subscription.Id,
                amount,
                payment.Currency,
                $"{payment.Purpose} payment",
                new Uri(command.SuccessUrl),
                new Uri(command.CancelUrl),
                savePaymentMethod,
                customerReference),
            cancellationToken);

        await sessions.AddAsync(
            OnlinePaymentSession.Create(payment, gateway.Name, session.SessionId, amount, timeProvider.GetUtcNow()),
            cancellationToken);

        return new CreateOnlineCheckoutResult(
            subscription.Id, payment.Id, gateway.Name, session.SessionId,
            session.RedirectUrl.ToString(), amount, payment.Currency);
    }
}
