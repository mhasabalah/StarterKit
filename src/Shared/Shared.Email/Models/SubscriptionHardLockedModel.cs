namespace Shared.Email.Models;

/// <summary>Pure data bag. Template identity and subjects live in nested <see cref="Metadata"/>.</summary>
public sealed class SubscriptionHardLockedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["SupportEmail"] = SupportEmail
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionHardLocked;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم تجميد حسابك"
            : "Your Account Has Been Locked";
    }
}
