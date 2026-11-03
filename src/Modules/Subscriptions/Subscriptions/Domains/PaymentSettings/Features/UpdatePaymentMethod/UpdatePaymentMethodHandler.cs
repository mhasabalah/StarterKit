using Subscriptions.Domains.Subscriptions.Services;
using System.Text.Json;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.UpdatePaymentMethod;

public class UpdatePaymentMethodCommandValidator : AbstractValidator<UpdatePaymentMethodCommand>
{
    public UpdatePaymentMethodCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Type).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Value).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500).When(x => x.Description is not null);
        RuleFor(x => x.Metadata).Must(m => m is null || IsValidJson(m)).WithMessage("Metadata must be valid JSON.");
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }

    private static bool IsValidJson(string value)
    {
        try { JsonDocument.Parse(value); return true; }
        catch { return false; }
    }
}

public class UpdatePaymentMethodHandler(
    IPaymentMethodRepository repository,
    SubscriptionsDbContext dbContext,
    IAdminAuditWriter audit)
    : ICommandHandler<UpdatePaymentMethodCommand, Unit>
{
    public async Task<Unit> Handle(UpdatePaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var method = await repository.GetByIdAsync(command.Id, cancellationToken)
            ?? throw new PaymentMethodNotFoundException(command.Id);

        dbContext.ExpectVersion(method, command.Version);
        var before = new { method.Type, method.DisplayName, method.Value, method.Description, method.SortOrder, method.IsActive };

        method.Update(
            command.Type,
            command.DisplayName,
            command.Value,
            command.Description,
            command.Metadata,
            command.SortOrder,
            command.IsActive);

        audit.Record("UpdatePaymentMethod", method.Id, "PaymentMethod", before: before,
            after: new { method.Type, method.DisplayName, method.Value, method.Description, method.SortOrder, method.IsActive });
        await repository.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
