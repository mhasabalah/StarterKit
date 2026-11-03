namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="InvoiceIssuedModel"/>.</summary>
internal sealed class InvoiceIssuedSubject : IEmailTemplate
{
    public Type ModelType => typeof(InvoiceIssuedModel);

    public EmailTemplate Template => EmailTemplate.InvoiceIssued;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "فاتورتك جاهزة للتحميل"
        : "Your Invoice Is Ready";
}
