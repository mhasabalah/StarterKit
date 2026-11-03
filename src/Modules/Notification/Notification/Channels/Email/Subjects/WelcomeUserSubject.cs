namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="WelcomeUserModel"/>.</summary>
internal sealed class WelcomeUserSubject(IOptions<EmailOptions> options) : IEmailTemplate
{
    public Type ModelType => typeof(WelcomeUserModel);

    private readonly string _brandName = options.Value.BrandName;

    public EmailTemplate Template => EmailTemplate.WelcomeUser;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? $"مرحباً بك في {_brandName}"
        : $"Welcome to {_brandName}";
}
