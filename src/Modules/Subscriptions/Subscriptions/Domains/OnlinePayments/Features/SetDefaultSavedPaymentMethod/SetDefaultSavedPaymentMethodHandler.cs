using Subscriptions.Domains.OnlinePayments.Exceptions;

namespace Subscriptions.Domains.OnlinePayments.Features.SetDefaultSavedPaymentMethod;

public record SetDefaultSavedPaymentMethodCommand(Guid SubscriberId, Guid MethodId) : ICommand<Unit>;

public class SetDefaultSavedPaymentMethodCommandValidator : AbstractValidator<SetDefaultSavedPaymentMethodCommand>
{
    public SetDefaultSavedPaymentMethodCommandValidator()
    {
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.MethodId).NotEmpty();
    }
}

public class SetDefaultSavedPaymentMethodHandler(ISavedPaymentMethodRepository methods)
    : ICommandHandler<SetDefaultSavedPaymentMethodCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultSavedPaymentMethodCommand command, CancellationToken cancellationToken)
    {
        // Another subscriber's method is reported as missing.
        var target = await methods.GetAsync(command.SubscriberId, command.MethodId, cancellationToken);
        if (target is not { IsActive: true })
            throw new SavedPaymentMethodNotFoundException(command.MethodId);
        if (target.IsDefault)
            return Unit.Value;

        // Two steps: the filtered unique index allows one default, so the old one is cleared first.
        var previous = await methods.GetDefaultAsync(command.SubscriberId, cancellationToken);
        if (previous is not null)
        {
            previous.SetDefault(false);
            await methods.SaveChangesAsync(cancellationToken);
        }

        target.SetDefault(true);
        await methods.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
