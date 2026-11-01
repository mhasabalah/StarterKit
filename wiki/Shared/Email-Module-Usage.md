# Email Module Usage Guide

This guide explains how to use the `Shared.Email` module in handlers/services and how to operate its admin endpoints.

## What the module provides

- Fire-and-forget email scheduling via `IEmailJobEnqueuer`
- Template rendering with localization (`en` / `ar`)
- Delivery through SMTP in production and no-op logging in non-production
- Email audit logging in `shared.email_log`
- Admin endpoints to list failed emails and re-enqueue

## Registration (already wired)

The module is already integrated in API startup:

```csharp
builder.Services.AddEmailInfrastructure(configuration);
...
app.UseEmailInfrastructure();
```

## Configuration

In `src/Api/appsettings.json`:

```json
"Email": {
  "SmtpHost": "smtp.office365.com",
  "SmtpPort": 587,
  "FromAddress": "noreply@example.com",
  "FromName": "StarterKit"
}
```

SMTP credentials are resolved from docker secrets/config keys:
- `smtp_username`
- `smtp_password`

## Main contract for business modules

Inject and use `IEmailJobEnqueuer` only.

```csharp
public sealed class SomeHandler(IEmailJobEnqueuer emailJobEnqueuer)
{
    public async Task Handle(CancellationToken ct)
    {
        var model = new DeveloperApprovedModel
        {
            CompanyName = "Acme",
            TempUsername = "acme-admin",
            TempPassword = "Temp#123",
            LoginUrl = "https://example.com/login"
        };

        var message = new EmailMessage(
            TemplateName: "DeveloperApproved",
            Model: model.ToDictionary(),
            Recipients: ["owner@acme.com"],
            Subject: "Your account is approved",
            Culture: EmailCulture.En);

        var jobId = await emailJobEnqueuer.EnqueueAsync(message, ct);
    }
}
```

## Re-enqueue failed email

```csharp
var newJobId = await emailJobEnqueuer.ReEnqueueAsync(emailLogId, ct);
```

Rules:
- re-enqueue works only when log status is `Failed`
- original log row is reused and set back to `Queued`

## Available templates

Use one of these template names:

- `DeveloperApplicationReceived`
- `DeveloperApproved`
- `DeveloperRejected`
- `PasswordResetRequired`
- `SubscriptionExpiringReminder`
- `SubscriptionExpired`
- `SubscriptionHardLocked`

## Admin API endpoints

Both require `AdminOnly` policy:

1. `GET /api/v{version}/email/failed`
   - returns failed emails in last 24h
2. `POST /api/v{version}/email/{id}/reenqueue`
   - re-enqueues one failed email log

## Queue and retry behavior

- Hangfire server config:
  - main server: `default`, `critical`
  - email server: `email` with worker count = `2`
- `EmailJob` has automatic retry (3 attempts)
- Dead-letter failures are logged by `EmailFailedStateFilter`

## Database/audit table

Audit rows are written to:
- schema: `shared`
- table: `email_log`

Key columns:
- `template_name`
- `recipient`
- `subject`
- `status` (`Queued`, `Sent`, `Failed`)
- `culture` (`En`, `Ar`)
- `hangfire_job_id`
- `message_json`

## Environment behavior

- **Production**: uses `MailKitEmailService` (real SMTP send)
- **Non-production**: uses `NoOpEmailService` (logs only, no send)

Template rendering runs in all environments.
