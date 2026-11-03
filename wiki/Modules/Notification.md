# Notification Module

`src/Modules/Notification` delivers messages to people over every channel (**Email**, **WhatsApp**), including
one-time codes that Identity issued (Notification never generates, stores or checks codes). It replaced the former `Shared.Email` + `Shared.Notification` projects: email is a *channel* of
Notification, not a separate service, so there is ONE dispatch pipeline, ONE retry policy and ONE delivery log.

| Project | Who references it | Contents |
|---|---|---|
| `Notification.Contracts` | every module that sends notifications | `INotificationSender` / `INotificationBuilder`, `INotificationRecipientResolver`, `INotificationLinkResolver`, `IOtpDeliveryService`, enums (`EmailTemplate`, `WhatsAppTemplate`, `NotificationChannel`, `NotificationCulture`, ...), email models (`Email/Models/*Model`), `EmailAttachment` |
| `Notification` | `Api` only (and test projects) | dispatch job, channels (MailKit/NoOp email, WhatsApp Cloud API), Liquid templates, subjects, options, `NotificationDbContext`, endpoints, `OtpDeliveryService` |
| `Notification.Tests` | - | unit tests + Postgres Testcontainers tests |

Modules never reference the implementation project (enforced by `Api.Tests/Architecture/ModuleBoundaryTests`).
Notification itself reads recipient contact data only through `UserDirectory.Contracts` (`GetUserContactInfoQuery`),
so it works with any identity provider.

## Sending a notification

```csharp
public sealed class SubscriptionActivatedEventHandler(INotificationSender notifications)
    : INotificationHandler<SubscriptionActivatedEvent>
{
    public async Task Handle(SubscriptionActivatedEvent e, CancellationToken ct)
    {
        var model = new SubscriptionActivatedModel { SubscriberName = "...", PlanName = "..." };

        await notifications
            .For(e.SubscriberId, NotificationCulture.En)          // a user id (or a tenant id, see below)
            .WithEmail(EmailTemplate.SubscriptionActivated, model.ToDictionary())
            .WithWhatsApp(WhatsAppTemplate.SubscriptionActivated)  // optional second channel
            .SendAsync(cancellationToken: ct);                     // enqueues; returns the correlation id
    }
}
```

- `For(userId)` - contact data is resolved at delivery time; the user's channel order (preferences) decides which
  channel is tried first; the other channels with a payload are the fallback.
- `ToEmail(address, culture, userId?)` - an explicit address (invitations, the CONFIRMED address of a password reset,
  admin digests). `userId` only links the log row.
- `SendAsync(NotificationChannel.Email)` - **force** a channel: preferences are ignored. Use it for security mail
  (password reset) that a preference must never suppress.
- `INotificationRecipientResolver` (implemented by Tenancy) maps a non-user recipient (a tenant subscriber) to the
  user who receives it (the tenant owner).
- `INotificationLinkResolver.ToAbsolute(url)` - links put into notifications: relative URLs are resolved against
  `Email:AppBaseUrl`.

There is no "send an email now" API for modules. The admin test endpoint is the only synchronous send.

## Delivery pipeline

`NotificationDispatchJob` (Hangfire queue `notification`, 4 retries at 30s/2m/10m/30m):

- **Permanent** failure (4xx, invalid recipient, bad template) - no retry, fall back to the next channel.
- **Transient** failure (5xx, 429, connection refused) - Hangfire retries; after `Notification:FallbackAfterAttempts`
  failures on a channel, fall back.
- **Ambiguous** failure (timeout) - retry the SAME channel only, never fall back (it may have been delivered).
- **Idempotent** - every attempt is a `notification.notification_log` row keyed by the correlation id; a re-run that
  finds a `Sent` row stops, so retries never send twice.
- The tenant of the enqueuing request is restored while the job runs (`TenantJobFilter`) and stamped on the log row.

Recurring jobs: `notification-daily-purge` (03:00, `Notification:LogRetentionDays`) and
`notification-daily-failure-summary` (07:00, warns in Seq with the number of notifications of the last 24 h that
were not delivered on any channel).

## Email templates

Adding a template = four pieces (startup validation fails if one is missing):

1. `EmailTemplate` value with `[TemplateFile("X")]` - `Notification.Contracts/Enums/EmailTemplate.cs`
2. Data model `XModel` with `ToDictionary()` - `Notification.Contracts/Email/Models/`
3. Subject class `XSubject : IEmailTemplate` (template, `ModelType`, localized subject) - `Notification/Channels/Email/Subjects/`
4. Liquid file `X.liquid` (embedded resource) - `Notification/Channels/Email/Templates/`

Globals available in every template: `culture` (`en`/`ar`), `brandName`, `appBaseUrl`, `supportEmail`,
`currentYear`. All `{{ output }}` is HTML-encoded; every `href` must use `| safe_url` (http/https and allow-listed
hosts only - `Email:AllowedLinkHosts`). In Development every template is smoke-rendered in both cultures at startup.

## One-time code delivery (OTP)

Identity owns the ONE OTP system (`IOtpCodeService`: issue, verify, attempts, cooldown, send cap, HMAC hashing - see
`wiki/Identity/README.md#one-time-codes-otp`). Notification only delivers:
`IOtpDeliveryService.SendAsync(userId, VerificationPurpose, code, culture)` - synchronous (not through the dispatch
job, so the caller can release an undelivered code), never stores or logs the code, returns a result instead of
throwing. Channel per purpose: `PhoneVerification` → WhatsApp (`PhoneVerificationOtp`), `EmailVerification` → Email
(no OTP email template yet → `ChannelUnavailable`), `TwoFactorLogin` → the user's preferred channel (WhatsApp
`TwoFactorOtp`). SMS codes (phone verification, password reset) go through Identity's `ISmsService`.

## Endpoints

| Route | Auth |
|---|---|
| `GET/PUT /api/v1/notifications/preferences` | the user |
| `GET/PUT /api/v1/notifications/preferences/{userId}` | `AdminOnly` |
| `GET /api/v1/notifications/history?userId=&channel=&status=` | `AdminOnly` - e.g. `?status=Failed&channel=Email` |
| `POST /api/v1/notifications/{id}/reenqueue` | `AdminOnly` - only `Failed` rows |
| `POST /api/v1/notifications/email/test` | `AdminOnly` - renders a template with sample data and sends it synchronously |
| `POST /api/v1/notifications/verification/send`, `/validate` | the user - implemented in Identity (`Domains/Verification`) on the unified OTP service; route and shapes unchanged |

## Configuration

| Section | Keys |
|---|---|
| `Email` | `Provider` (`Auto`/`Smtp`/`NoOp`; Auto = NoOp in Development without a real SMTP host), `SmtpHost`, `SmtpPort`, `UseTls`, `FromAddress`, `FromName`, `BrandName`, `AppBaseUrl`, `SupportEmail`, `AllowedLinkHosts` |
| `Notification` | `DefaultChannelOrder`, `FallbackAfterAttempts`, `LogRetentionDays`, `LogPurgeBatchSize` |
| `WhatsApp` | `ApiVersion`, `Templates` (one entry per `WhatsAppTemplate`, validated at startup) |

Secrets (Docker secrets / config keys): `smtp_username`, `smtp_password`, `whatsapp_access_token`,
`whatsapp_phone_number_id`. (The OTP HMAC key is Identity's `identity_otp_hmac_key`.)

## Database

`NotificationDbContext`, schema `notification`, history table `__notification_migrations_history`:
`notification_log` (every delivery attempt of every channel) and `user_notification_preference`. A host-database
context (not `ITenantDataContext`): rows carry a nullable `tenant_id` and are deleted by the tenant purge
(`TenantDataPurgeRequestedEvent`).

```bash
dotnet ef migrations add <Name> --context NotificationDbContext \
  --project src/Modules/Notification/Notification --startup-project src/Api --output-dir Data/Migrations
```
