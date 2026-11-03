namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>The payment method details copied onto a payment when its proof is submitted.</summary>
public sealed record PaymentMethodSnapshot(
    Guid? Id,
    string? Type,
    string? DisplayName,
    string? Value,
    string? Description)
{
    public static PaymentMethodSnapshot From(PaymentMethod method) =>
        new(method.Id, method.Type, method.DisplayName, method.Value, method.Description);
}
