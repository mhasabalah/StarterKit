using System.Text.Json;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.AddPaymentMethod;

public class AddPaymentMethodCommandValidator : AbstractValidator<AddPaymentMethodCommand>
{
    public AddPaymentMethodCommandValidator()
    {
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

public class AddPaymentMethodHandler(IPaymentMethodRepository repository)
    : ICommandHandler<AddPaymentMethodCommand, Guid>
{
    public async Task<Guid> Handle(AddPaymentMethodCommand command, CancellationToken cancellationToken)
    {
        var method = PaymentMethod.Create(
            command.Type,
            command.DisplayName,
            command.Value,
            command.Description,
            command.Metadata,
            command.SortOrder);

        await repository.AddAsync(method, cancellationToken);
        return method.Id;
    }
}
