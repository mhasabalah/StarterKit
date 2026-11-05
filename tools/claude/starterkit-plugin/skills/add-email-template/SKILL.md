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
| 4 | `Notification/Channels/Email/Templates/<X>.liquid` | Body only; the layout wraps it. Branch on `{% if culture == 'ar' %}` and build from `Components/{Heading,Text,Button,Alert,Card,KeyValue,CodeBlock,Badge,Divider,Spacer}`. Colors only as tokens (`{{ t.textMuted }}` - never hex; `EmailDesignSystemTests` fails), alignment via `{{ align }}`/`{{ dir }}`, preview text via `{{ '...' | set_preheader }}` (no secrets). Copy `SecurityAlert.liquid`. Guide: `wiki/Modules/Notification-Email-Design-System.md`. Embedded by the `Templates\**\*.liquid` glob. |

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

## Also in-app and push? (no template needed)

If the user should also see it in the app inbox (real-time) and/or get a phone/web push, add the ADDITIVE channels
on the same builder. They need no template files, only bilingual text:

```csharp
await notifications
    .For(userId, culture)
    .WithEmail(EmailTemplate.<X>, model.ToDictionary())
    .WithInApp(new InAppContent(
        new LocalizedText("<title en>", "<title ar>"),
        new LocalizedText("<body en>", "<body ar>"),
        Category: "<module>.<event>",                 // e.g. "billing.invoice-issued"; clients route by it
        ActionUrl: "/<app route>",                    // relative, or a host in Email:AllowedLinkHosts
        Data: new Dictionary<string, string> { ["<id>"] = id.ToString() }))   // ids only, no PII
    .WithPush()                                       // same text; WithPush(sensitive: true) for personal content
    .SendAsync(cancellationToken: ct);
```

- They are delivered IN ADDITION to the email (never as fallback) and need a user recipient.
- Never put `InApp`/`Push` in a channel order; `SendAsync(NotificationChannel.Push)` sends ONLY the push.
- Test: assert on your fake `INotificationBuilder` (it must implement `WithInApp` / `WithPush`).
- Guide: `wiki/Modules/Notification-Realtime-and-Push.md`.

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Modules/Notification/Notification.Tests       # registry/renderer completeness tests
```

Then run the API with Mailhog (`docker compose up -d`, UI at `http://localhost:8025`). Trigger the flow and check
both cultures (`Accept-Language: ar`), RTL layout, and every link.
