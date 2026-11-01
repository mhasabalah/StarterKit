namespace Subscriptions.Contracts.Subscriptions.Commands;

public record UpsertPaymentInstructionsCommand(
    string SupportWhatsApp,
    string SupportEmail) : ICommand<Unit>;
