using UserDirectory.Contracts.Queries;
using Microsoft.Extensions.Options;
using Notification.Contracts;
using Notification.Contracts.Email.Models;
using Notification.Contracts.Enums;

namespace Tenancy.Domains.Invitations.Services;

/// <summary>
/// Queues the invitation e-mail (template <c>TenantInvitation</c>) AFTER the invitation was committed.
/// <para>
/// Fail-soft: a failure to queue is logged and reported (<c>EmailQueued = false</c>) instead of failing the request -
/// the invitation exists and <c>POST /tenant-invitations/{id}/resend</c> issues a new link. The link carries the raw
/// token; a leaked link alone is useless (acceptance needs a signed-in user with the same VERIFIED e-mail).
/// </para>
/// </summary>
public sealed class TenantInvitationMailer(
    INotificationSender notifications,
    INotificationLinkResolver links,
    ISender sender,
    IOptions<MultiTenancyOptions> tenancyOptions,
    ILogger<TenantInvitationMailer> logger)
{
    public async Task<bool> SendAsync(TenantInvitation invitation, string token, TenantInfo tenant, Guid inviterUserId,
        DateTime utcNow, CancellationToken cancellationToken)
    {
        if (!tenancyOptions.Value.Invitations.SendEmail)
            return false;

        try
        {
            var inviter = (await sender.Send(new GetUserSummariesQuery([inviterUserId]), cancellationToken))
                .FirstOrDefault();

            var model = new TenantInvitationModel
            {
                InviterName = inviter is { DisplayName.Length: > 0 } ? inviter.DisplayName : inviter?.Email ?? tenant.Name,
                TenantName = tenant.Name,
                AcceptUrl = BuildAcceptUrl(token),
                ExpiresInDays = Math.Max(1, (int)Math.Ceiling((invitation.ExpiresAt - utcNow).TotalDays))
            };

            // The invitee may not be a user yet: address the invitation e-mail itself. The delivery log is stamped with
            // the ambient tenant (invitations are tenant-scoped requests).
            await notifications
                .ToEmail(invitation.Email, CultureOf(tenant))
                .WithEmail(EmailTemplate.TenantInvitation, model.ToDictionary())
                .SendAsync(NotificationChannel.Email, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Queuing the e-mail of invitation {InvitationId} failed; it can be re-sent",
                invitation.Id);
            return false;
        }
    }

    /// <summary><c>AcceptUrl</c> with <c>{token}</c> replaced; relative URLs are resolved against the notification app URL.</summary>
    public string BuildAcceptUrl(string token) =>
        links.ToAbsolute(tenancyOptions.Value.Invitations.AcceptUrl.Replace("{token}", Uri.EscapeDataString(token),
            StringComparison.Ordinal));

    private static NotificationCulture CultureOf(TenantInfo tenant) =>
        tenant.Culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true
            ? NotificationCulture.Ar
            : NotificationCulture.En;
}
