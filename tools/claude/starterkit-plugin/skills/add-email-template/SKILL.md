---
name: add-email-template
description: Add a new transactional email (and optionally a WhatsApp message) to the StarterKit Notification module - EmailTemplate enum value, model with ToDictionary, localized subject class, Liquid template using the shared layout/components, and sending it from another module through INotificationSender. Use for "send an email when", "new email template", "notify the user by email/WhatsApp".
---

# Add an email template

All delivery goes through the Notification module (`wiki/Modules/Notification.md`). Other modules never send mail
directly. They inject `INotificationSender` from `Notification.Contracts`.

Startup **fails** if any part is missing: `EmailTemplateRegistry` validates that every enum value has a subject class,
and the renderer validates that the `.liquid` file exists.

## The four parts

Templates: [references/templates.md](references/templates.md).

| # | File | Rule |
|---|---|---|
| 1 | `Notification.Contracts/Enums/EmailTemplate.cs` | **Append** `[TemplateFile("<X>")] <X>` at the END, never insert: numeric values are stored in the delivery log. |
| 2 | `Notification.Contracts/Email/Models/<X>Model.cs` | `sealed class`, plain properties, `Dictionary<string, object> ToDictionary()`. Pure data. The brand name is a template global (`{{ brandName }}`), not a model field. |
| 3 | `Notification/Channels/Email/Subjects/<X>Subject.cs` | `internal sealed class : IEmailTemplate`, with `ModelType`, `Template`, and `GetSubject(culture)` for **en and ar**. Discovered automatically by assembly scan. |
| 4 | `Notification/Channels/Email/Templates/<X>.liquid` | Body only; the layout wraps it. Branch on `{% if culture == 'ar' %}` and reuse `Components/{Button,Alert,Card,Divider,Spacer}`. Inline styles only (email clients). Embedded by the `Templates\**\*.liquid` glob. |

Optional WhatsApp: add a `WhatsAppTemplate` enum value and an entry in `Notification:WhatsApp:Templates`
(validated at startup). The template itself must be approved in Meta Business Manager.

## Send it (from any module)

```csharp
await notifications
    .For(userId, NotificationCulture.En)                    // contact data resolved at delivery; user's channel order
    .WithEmail(EmailTemplate.<X>, model.ToDictionary())
    .SendAsync(cancellationToken: ct);                      // enqueued; returns a correlation id
```

- Pass an explicit address with `.ToEmail(address, culture, userId?)` (invitations, confirmed addresses).
- **Security mail** (password, 2FA, email change) must force the channel: `.SendAsync(NotificationChannel.Email, ct)`.
- Send from a **domain event handler** or a consumer, after commit. Never send before `SaveChanges`.
- Users' names and contact details come from `UserDirectory.Contracts` (`GetUserContactInfoQuery`), never from
  Identity directly.
- Links: relative URLs are resolved against `Email:AppBaseUrl` through `INotificationLinkResolver`.

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Modules/Notification/Notification.Tests       # registry/renderer completeness tests
```

Then run the API with Mailhog (`docker compose up -d`, UI at `http://localhost:8025`). Trigger the flow and check
both cultures (`Accept-Language: ar`), RTL layout, and every link.
