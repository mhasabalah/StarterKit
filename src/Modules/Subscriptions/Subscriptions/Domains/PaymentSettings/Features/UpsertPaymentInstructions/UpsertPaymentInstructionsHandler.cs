using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.PaymentSettings.Features.UpsertPaymentInstructions;

public class UpsertPaymentInstructionsCommandValidator
    : AbstractValidator<UpsertPaymentInstructionsCommand>
{
    public UpsertPaymentInstructionsCommandValidator()
    {
        RuleFor(x => x.SupportWhatsApp).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SupportEmail).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

public class UpsertPaymentInstructionsHandler(IPaymentInstructionsRepository repository)
    : ICommandHandler<UpsertPaymentInstructionsCommand, Unit>
{
    public async Task<Unit> Handle(
        UpsertPaymentInstructionsCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(cancellationToken);

        if (existing is null)
        {
            var instructions = PaymentInstructions.Create(
                command.SupportWhatsApp,
                command.SupportEmail);
            await repository.AddAsync(instructions, cancellationToken);
        }
        else
        {
            existing.Update(command.SupportWhatsApp, command.SupportEmail);
            await repository.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
