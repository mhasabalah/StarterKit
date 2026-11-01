using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.DeletePaymentMethod;

public class DeletePaymentMethodHandler(IPaymentMethodRepository repository)
    : ICommandHandler<DeletePaymentMethodCommand, Unit>
{
    public async Task<Unit> Handle(DeletePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var method = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new PaymentMethodNotFoundException(command.Id);

        await repository.DeleteAsync(method, cancellationToken);
        return Unit.Value;
    }
}
