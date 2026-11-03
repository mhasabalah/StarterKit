# add-email-template templates

These are copied from the `EmailConfirmation` template, the newest and cleanest example.

## 1. Enum value: append at the end

```csharp
    // Appended (never inserted) so stored numeric values of existing templates stay stable.
    [TemplateFile("EmailConfirmation")]
    EmailConfirmation,

    [TemplateFile("{{Template}}")]
    {{Template}}
}
```

## 2. Model (`Notification.Contracts/Email/Models/{{Template}}Model.cs`)

```csharp
namespace Notification.Contracts.Email.Models;

/// <summary>Pure data bag; subject and template file live in the Notification module.</summary>
public sealed class {{Template}}Model
{
    public string FirstName { get; set; } = string.Empty;
    public string ActionUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["FirstName"] = FirstName,
        ["ActionUrl"] = ActionUrl
    };
}
```

## 3. Subject (`Notification/Channels/Email/Subjects/{{Template}}Subject.cs`)

```csharp
namespace Notification.Channels.Email.Subjects;

internal sealed class {{Template}}Subject : IEmailTemplate
{
    public Type ModelType => typeof({{Template}}Model);

    public EmailTemplate Template => EmailTemplate.{{Template}};

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "عنوان الرسالة بالعربية"
        : "Subject in English";
}
```

## 4. Liquid body (`Notification/Channels/Email/Templates/{{Template}}.liquid`)

```liquid
{% comment %}Variables: FirstName, ActionUrl{% endcomment %}

<h2 style="margin:0 0 16px 0;font-size:22px;font-weight:700;color:#0f172a;line-height:1.35;">
  {% if culture == 'ar' %}العنوان{% else %}Heading{% endif %}
</h2>

<p style="margin:0 0 20px 0;font-size:15px;color:#4b5563;line-height:1.75;">
  {% if culture == 'ar' %}
    مرحباً <strong style="color:#0f172a;">{{ FirstName }}</strong> ...
  {% else %}
    Hello <strong style="color:#0f172a;">{{ FirstName }}</strong>, ... {{ brandName }} ...
  {% endif %}
</p>

{% assign button_url = ActionUrl %}
{% assign button_variant = 'primary' %}
{% if culture == 'ar' %}{% assign button_text = 'متابعة' %}{% else %}{% assign button_text = 'Continue' %}{% endif %}
{% include 'Components/Button' %}
```

## Sender (in the module that owns the business fact, for example a domain event handler)

```csharp
public sealed class {{Aggregate}}ApprovedEventHandler(INotificationSender notifications, ISender sender)
    : INotificationHandler<{{Aggregate}}ApprovedEvent>
{
    public async Task Handle({{Aggregate}}ApprovedEvent e, CancellationToken ct)
    {
        var contact = await sender.Send(new GetUserContactInfoQuery(e.OwnerId), ct);   // UserDirectory.Contracts
        if (contact is null) return;

        var model = new {{Template}}Model { FirstName = contact.FirstName ?? string.Empty, ActionUrl = $"/{{aggregate}}s/{e.Id}" };
        await notifications.For(e.OwnerId, NotificationCulture.En)
            .WithEmail(EmailTemplate.{{Template}}, model.ToDictionary())
            .SendAsync(cancellationToken: ct);
    }
}
```

Before using `GetUserContactInfoQuery`, check its exact shape and result fields in
`src/Modules/Identity/UserDirectory.Contracts`.
