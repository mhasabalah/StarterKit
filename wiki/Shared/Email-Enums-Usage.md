# Email Enums Usage Guide

This guide explains how to use the new strongly-typed email enums in `Shared.Email`.

## Enums

### `EmailDeliveryStatus`

File: `src/Shared/Shared.Email/Enums/EmailDeliveryStatus.cs`

```csharp
public enum EmailDeliveryStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2
}
```

Used by `EmailLog.Status` and repository status transitions.

### `EmailCulture`

File: `src/Shared/Shared.Email/Enums/EmailCulture.cs`

```csharp
public enum EmailCulture
{
    En = 0,
    Ar = 1
}
```

Used by `EmailMessage.Culture` and `EmailLog.Culture`.

## Create and enqueue an email

Use `IEmailJobEnqueuer` with `EmailMessage` and pass enum values directly.

```csharp
public sealed class SendWelcomeEmailHandler(IEmailJobEnqueuer emailJobEnqueuer)
{
    public async Task<string> Handle(CancellationToken ct)
    {
        var message = new EmailMessage(
            TemplateName: "DeveloperApproved",
            Model: new Dictionary<string, object>
            {
                ["CompanyName"] = "Acme",
                ["TempUsername"] = "acme-admin",
                ["TempPassword"] = "Temp#12345",
                ["LoginUrl"] = "https://example.com/login"
            },
            Recipients: ["owner@acme.com"],
            Subject: "Your account is approved",
            Culture: EmailCulture.En);

        return await emailJobEnqueuer.EnqueueAsync(message, ct);
    }
}
```

## Culture behavior in templates

`FluidTemplateRenderer` converts enum culture to template values:

- `EmailCulture.En` -> `"en"`
- `EmailCulture.Ar` -> `"ar"`

Templates still use:

```liquid
{% if culture == 'ar' %} ... {% else %} ... {% endif %}
```

No template changes are required.

## Status transitions

`EmailDeliveryStatus` is now used instead of magic strings:

- On enqueue: `Queued`
- On successful send: `Sent`
- On failure/retries exhausted: `Failed`
- Re-enqueue allowed only when status is `Failed`

## Persistence details

In `EmailDbContext`, both enums are stored as **strings** in PostgreSQL:

```csharp
entity.Property(x => x.Status).HasConversion<string>();
entity.Property(x => x.Culture).HasConversion<string>();
```

This keeps database values readable and stable.

## API response impact

`GET /api/v{version}/email/failed` now returns enum-backed values for:

- `culture` (`En` / `Ar`)
- `status` (`Queued` / `Sent` / `Failed`)

If frontend expects lowercase string values, map enums explicitly in the API DTO layer.
