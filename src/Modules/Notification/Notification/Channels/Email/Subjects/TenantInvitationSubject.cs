namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="TenantInvitationModel"/>.</summary>
internal sealed class TenantInvitationSubject : IEmailTemplate
{
    public Type ModelType => typeof(TenantInvitationModel);

    public EmailTemplate Template => EmailTemplate.TenantInvitation;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "لديك دعوة للانضمام إلى فريق"
        : "You have been invited to join a team";
}
