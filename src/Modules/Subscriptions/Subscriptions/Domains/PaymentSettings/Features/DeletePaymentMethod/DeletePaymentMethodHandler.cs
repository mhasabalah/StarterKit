using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.DeletePaymentMethod;

public class DeletePaymentMethodHandler(IPaymentMethodRepository repository, IAdminAuditWriter audit)
    : ICommandHandler<DeletePaymentMethodCommand, Unit>
{
    public async Task<Unit> Handle(DeletePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var method = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new PaymentMethodNotFoundException(command.Id);

        audit.Record("DeletePaymentMethod", method.Id, "PaymentMethod",
            before: new { method.Type, method.DisplayName, method.Value, method.Description, method.SortOrder, method.IsActive });
        await repository.DeleteAsync(method, cancellationToken);
        return Unit.Value;
    }
}
