# Notification Module Usage Guide

This guide explains how modules communicate with `Shared.Notification`, and shows an auth verification flow where the user prefers **WhatsApp**.

## What `Shared.Notification` provides

- One entry point for all modules: `INotificationJobEnqueuer`
- Multi-channel dispatch with fallback (default order: WhatsApp then Email)
- User-level channel preference (`/api/v1/notifications/preferences`)
- Delivery audit log + admin history/re-enqueue endpoints

## How modules contact Notification

Any module (Identity, Developers, Properties, etc.) should only depend on:

- `INotificationJobEnqueuer`
- `NotificationMessage`

Example from any handler:

```csharp
public sealed class SomeHandler(INotificationJobEnqueuer notificationJobEnqueuer)
{
    public async Task Handle(Guid userId, CancellationToken ct)
    {
        var message = new NotificationMessage(
            UserId: userId,
            TemplateName: "DeveloperApproved",
            Model: new Dictionary<string, object>
            {
                ["DeveloperName"] = "Ali"
            },
            Subject: "Application approved"
        );

        await notificationJobEnqueuer.EnqueueAsync(message, ct);
    }
}
```

The module does **not** pass email/phone directly. `NotificationDispatchJob` resolves contact data internally through `GetUserContactInfoQuery` in Identity.

## User preference APIs (channel order)

- `GET /api/v1/notifications/preferences` (authenticated user)
- `PUT /api/v1/notifications/preferences` (authenticated user)
- `GET /api/v1/notifications/preferences/{userId}` (admin)
- `PUT /api/v1/notifications/preferences/{userId}` (admin)

Example request:

```json
{
  "channelOrder": ["WhatsApp", "Email"]
}
```

## Example flow: Auth verification + user chooses WhatsApp

> Current `SendPhoneVerificationHandler` sends SMS directly.  
> This section shows the recommended flow if verification is routed through `Shared.Notification`.

1. User logs in and sets preference to WhatsApp first:
   - `PUT /api/v1/notifications/preferences`
   - body: `{"channelOrder":["WhatsApp","Email"]}`
2. User requests phone verification:
   - `POST /api/identity/phone/send-verification`
3. Auth handler generates verification code, then enqueues notification:

```csharp
var message = new NotificationMessage(
    UserId: command.UserId,
    TemplateName: "PhoneVerificationCode",
    Model: new Dictionary<string, object>
    {
        ["Code"] = code,
        ["ExpiryMinutes"] = _options.CodeExpirationMinutes
    },
    Subject: "Phone verification code");

await notificationJobEnqueuer.EnqueueAsync(message, cancellationToken);
```

4. Dispatcher resolves user contact info from Identity (`Email`, `Phone`).
5. Dispatcher loads user preference and attempts WhatsApp first.
6. If WhatsApp cannot send, it falls back to Email.
7. User submits code to:
   - `POST /api/identity/phone/verify`
8. Verification handler validates code and marks phone as verified.

## Required configuration

In `src/Api/appsettings.json`:

```json
"Notification": {
  "DefaultChannelOrder": ["WhatsApp", "Email"]
},
"WhatsApp": {
  "ApiVersion": "v21.0",
  "PhoneNumberId": "",
  "AccessToken": "",
  "Templates": {
    "PhoneVerificationCode": {
      "MetaTemplateName": "phone_verification_code",
      "LanguageCode": "en_US",
      "BodyParameters": ["Code", "ExpiryMinutes"]
    }
  }
}
```

Secrets:

- `whatsapp_access_token`
- `whatsapp_phone_number_id`

## Admin operations

- `GET /api/v1/notifications/history` (admin): inspect cross-channel delivery
- `POST /api/v1/notifications/{id}/reenqueue` (admin): retry failed delivery

