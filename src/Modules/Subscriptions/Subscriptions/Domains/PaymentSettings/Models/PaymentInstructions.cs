namespace Subscriptions.Domains.PaymentSettings.Models;

public class PaymentInstructions : Aggregate<Guid>
{
    public static readonly Guid SingletonId = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    public string SupportWhatsApp { get; private set; } = default!;
    public string SupportEmail { get; private set; } = default!;

    private PaymentInstructions() { }

    public static PaymentInstructions Create(
        string supportWhatsApp,
        string supportEmail) => new()
        {
            Id = SingletonId,
            SupportWhatsApp = supportWhatsApp,
            SupportEmail = supportEmail
        };

    public void Update(string supportWhatsApp, string supportEmail)
    {
        SupportWhatsApp = supportWhatsApp;
        SupportEmail = supportEmail;
    }
}
