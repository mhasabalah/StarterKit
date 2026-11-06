using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Domains.OnlinePayments.Gateways;

namespace Subscriptions.Domains.OnlinePayments.Features.RemoveSavedPaymentMethod;

public record RemoveSavedPaymentMethodCommand(Guid SubscriberId, Guid MethodId) : ICommand<Unit>;

public class RemoveSavedPaymentMethodCommandValidator : AbstractValidator<RemoveSavedPaymentMethodCommand>
{
    public RemoveSavedPaymentMethodCommandValidator()
    {
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.MethodId).NotEmpty();
    }
}

/// <summary>
/// Revokes the method locally FIRST (so it is never charged again, whatever the provider does), then detaches it at
/// the provider on a best-effort basis. When the removed method was the default, the newest remaining one takes over.
/// </summary>
internal sealed class RemoveSavedPaymentMethodHandler(
    ISavedPaymentMethodRepository methods,
    IPaymentGatewayRegistry gateways,
    TimeProvider timeProvider,
    ILogger<RemoveSavedPaymentMethodHandler> logger)
    : ICommandHandler<RemoveSavedPaymentMethodCommand, Unit>
{
    public async Task<Unit> Handle(RemoveSavedPaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var method = await methods.GetAsync(command.SubscriberId, command.MethodId, cancellationToken);
        if (method is not { IsActive: true })
            throw new SavedPaymentMethodNotFoundException(command.MethodId);

        var wasDefault = method.IsDefault;
        method.Revoke(timeProvider.GetUtcNow());
        await methods.SaveChangesAsync(cancellationToken);

        if (wasDefault)
        {
            var next = (await methods.GetActiveAsync(command.SubscriberId, cancellationToken)).FirstOrDefault();
            if (next is not null)
            {
                next.SetDefault(true);
                await methods.SaveChangesAsync(cancellationToken);
            }
        }

        try
        {
            await gateways.Get(method.Gateway).DetachAsync(method.ProviderCustomerRef, method.ProviderMethodRef, cancellationToken);
        }
        catch (Exception ex) when (ex is PaymentGatewayException or PaymentGatewayNotFoundException)
        {
            logger.LogWarning(ex, "Saved method {MethodId} was removed locally but could not be detached at {Gateway}.",
                method.Id, method.Gateway);
        }

        return Unit.Value;
    }
}
